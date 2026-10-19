using NModbus;
using NModbus.Serial;
using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace thinger.Mexico50Towdolly.ModbusRTU
{
    /// <summary>
    /// Modbus RTU 通讯服务类 (工业现场终极版：解决频繁开关串口死锁、完善浮点解析)
    /// </summary>
    public class ModbusRTUService : IDisposable
    {
        private volatile bool _disposed = false;
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private readonly ModbusFactory _factory = new ModbusFactory();
        private SerialPort? _serialPort;
        private IModbusSerialMaster? _master;

        public Action<string>? LogAction { get; set; }

        public string PortName { get; private set; } = "COM1";
        public int BaudRate { get; private set; } = 9600;
        public int DataBits { get; private set; } = 8;
        public Parity Parity { get; private set; } = Parity.None;
        public StopBits StopBits { get; private set; } = StopBits.One;

        public int ReadTimeoutMs { get; set; } = 1000;
        public int WriteTimeoutMs { get; set; } = 1000;
        public int MaxAttempts { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 500;

        // 仅当串口对象存在且物理上为打开状态时才为 true
        public bool IsConnected => _serialPort != null && _serialPort.IsOpen && _master != null;

        // ========================================================================
        // 1. 连接与断开
        // ========================================================================

        public async Task ConnectAsync(string portName, int baudRate, int dataBits, Parity parity, StopBits stopBits, CancellationToken ct = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ModbusRTUService));

            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                PortName = portName; BaudRate = baudRate; DataBits = dataBits; Parity = parity; StopBits = stopBits;

                if (IsConnected) return;

                await Task.Run(() => ConnectCore(), ct).ConfigureAwait(false);
                LogAction?.Invoke($"[Modbus RTU] 成功打开串口 {PortName} ({BaudRate})");
            }
            catch (Exception ex)
            {
                LogAction?.Invoke($"[Modbus RTU] 串口连接失败: {ex.Message}");
                throw;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task DisconnectAsync()
        {
            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                DisconnectCore();
                LogAction?.Invoke($"[Modbus RTU] 已主动关闭串口 {PortName}");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private void ConnectCore()
        {
            DisconnectCore();

            _serialPort = new SerialPort(PortName)
            {
                BaudRate = BaudRate,
                DataBits = DataBits,
                Parity = Parity,
                StopBits = StopBits,
                ReadTimeout = ReadTimeoutMs,
                WriteTimeout = WriteTimeoutMs
            };

            _serialPort.Open();

            var adapter = new SerialPortAdapter(_serialPort);
            _master = _factory.CreateRtuMaster(adapter);
            _master.Transport.Retries = 0;
            _master.Transport.ReadTimeout = ReadTimeoutMs;
            _master.Transport.WriteTimeout = WriteTimeoutMs;
        }

        private void DisconnectCore()
        {
            if (_master is IDisposable m) { try { m.Dispose(); } catch { } }
            _master = null;

            if (_serialPort != null)
            {
                try { if (_serialPort.IsOpen) _serialPort.Close(); } catch { }
                try { _serialPort.Dispose(); } catch { }
            }
            _serialPort = null;
        }

        // ========================================================================
        // 2. 读写操作封装 (8大功能全集补齐)
        // ========================================================================

        public Task<bool[]> ReadCoilsAsync(byte slaveId, ushort startAddress, ushort numCoils, CancellationToken ct = default)
            => ExecuteWithRetryAsync(m => m.ReadCoils(slaveId, startAddress, numCoils), $"读取线圈(01)", ct);

        public Task<bool[]> ReadDiscreteInputsAsync(byte slaveId, ushort startAddress, ushort numInputs, CancellationToken ct = default)
            => ExecuteWithRetryAsync(m => m.ReadInputs(slaveId, startAddress, numInputs), $"读取离散输入(02)", ct);

        public Task<ushort[]> ReadHoldingRegistersAsync(byte slaveId, ushort startAddress, ushort numRegisters, CancellationToken ct = default)
            => ExecuteWithRetryAsync(m => m.ReadHoldingRegisters(slaveId, startAddress, numRegisters), $"读取保持寄存器(03)", ct);

        public Task<ushort[]> ReadInputRegistersAsync(byte slaveId, ushort startAddress, ushort numRegisters, CancellationToken ct = default)
            => ExecuteWithRetryAsync(m => m.ReadInputRegisters(slaveId, startAddress, numRegisters), $"读取输入寄存器(04)", ct);

        public Task WriteSingleCoilAsync(byte slaveId, ushort coilAddress, bool value, CancellationToken ct = default)
            => ExecuteWithRetryAsync(m => { m.WriteSingleCoil(slaveId, coilAddress, value); return true; }, $"写单线圈(05)", ct);

        public Task WriteSingleRegisterAsync(byte slaveId, ushort registerAddress, ushort value, CancellationToken ct = default)
            => ExecuteWithRetryAsync(m => { m.WriteSingleRegister(slaveId, registerAddress, value); return true; }, $"写单寄存器(06)", ct);

        // 【功能补齐】功能码 15 - 写多个线圈
        public Task WriteMultipleCoilsAsync(byte slaveId, ushort startAddress, bool[] data, CancellationToken ct = default)
            => ExecuteWithRetryAsync(m => { m.WriteMultipleCoils(slaveId, startAddress, data); return true; }, $"写多线圈(15)", ct);

        // 【功能补齐】功能码 16 - 写多个寄存器 (用于写入浮点数或 32 位长整数)
        public Task WriteMultipleRegistersAsync(byte slaveId, ushort startAddress, ushort[] data, CancellationToken ct = default)
            => ExecuteWithRetryAsync(m => { m.WriteMultipleRegisters(slaveId, startAddress, data); return true; }, $"写多寄存器(16)", ct);

        // ========================================================================
        // 3. 核心执行引擎 (修复致命重试坑点)
        // ========================================================================

        private async Task<T> ExecuteWithRetryAsync<T>(Func<IModbusMaster, T> action, string operationName, CancellationToken ct)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException($"[Modbus RTU] 串口 {PortName} 尚未连接，请先调用 ConnectAsync()！");
            }

            if (_disposed) throw new ObjectDisposedException(nameof(ModbusRTUService));

            await _ioLock.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                Exception? lastException = null;

                for (int attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    if (ct.IsCancellationRequested) throw new TaskCanceledException();

                    try
                    {
                        // 丢包重试时，绝不频繁 Close/Open！
                        if (attempt > 1)
                        {
                            LogAction?.Invoke($"[Modbus RTU] {operationName} 失败，准备第 {attempt} 次重试...");

                            // 只有当硬件级别的串口彻底关闭了，才重新初始化
                            if (!IsConnected)
                            {
                                await Task.Run(() => ConnectCore(), ct).ConfigureAwait(false);
                            }
                            else
                            {
                                // 仅仅清空缓冲区，防止上次残留的乱码干扰本次报文
                                try
                                {
                                    _serialPort?.DiscardInBuffer();
                                    _serialPort?.DiscardOutBuffer();
                                }
                                catch { }

                                // 【新增静默间隔】：等待 20ms，给半双工的 485 芯片和慢速从机留出切换响应的时间
                                await Task.Delay(20, ct).ConfigureAwait(false);
                            }
                        }

                        return await Task.Run(() => action(_master!), ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (IsSerialConnectionException(ex))
                    {
                        lastException = ex;
                        LogAction?.Invoke($"[Modbus RTU警告] {operationName}: {ex.Message}");
                        if (attempt == MaxAttempts) break;

                        try { await Task.Delay(RetryDelayMs, ct).ConfigureAwait(false); }
                        catch (TaskCanceledException) { break; }
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                }

                throw new IOException($"RTU 操作彻底失败: {lastException?.Message}", lastException);
            }
            finally
            {               
                // 异步延时，不占线程资源，且依然保持持锁状态
                try { await Task.Delay(10, ct).ConfigureAwait(false); } catch { }
                _ioLock.Release();
            }
        }

        private static bool IsSerialConnectionException(Exception ex)
        {
            return ex is TimeoutException || ex is IOException || ex is InvalidOperationException || ex is UnauthorizedAccessException || (ex.InnerException != null && IsSerialConnectionException(ex.InnerException));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _ioLock.Wait(200); } catch { }
            DisconnectCore();
            _ioLock.Dispose();
        }

        // ========================================================================
        // 4. 浮点数解析辅助 (修复参数歧义坑点)
        // ========================================================================

        public enum ModbusFloatByteOrder { BigEndian, LittleEndian, BigEndianSwap, LittleEndianSwap }

        /// <summary>
        /// 直接传入数组和起始索引，按寄存器地址自然顺序解析
        /// </summary>
        /// <param name="registers">Modbus 读取回来的原始寄存器数组</param>
        /// <param name="startIndex">浮点数存放的起始地址索引 (通常是 0)</param>
        /// <param name="order">字节序</param>
        public float RegistersToFloat(ushort[] registers, int startIndex, ModbusFloatByteOrder order)
        {
            if (registers == null || registers.Length < startIndex + 2)
                throw new ArgumentException("解析浮点数失败：寄存器数组为空或长度不足 2 个字。");

            // 按自然地址顺序获取：word1 是低地址 (N), word2 是高地址 (N+1)
            ushort word1 = registers[startIndex];
            ushort word2 = registers[startIndex + 1];

            // 拆分为 4 个单字节
            byte w1Hi = (byte)(word1 >> 8); byte w1Lo = (byte)(word1 & 0xFF);
            byte w2Hi = (byte)(word2 >> 8); byte w2Lo = (byte)(word2 & 0xFF);

            byte[] bytes = new byte[4];

            // 按 Modbus 标准协议重新拼接字节
            switch (order)
            {
                case ModbusFloatByteOrder.BigEndian:       // ABCD
                    bytes[0] = w1Hi; bytes[1] = w1Lo; bytes[2] = w2Hi; bytes[3] = w2Lo; break;

                case ModbusFloatByteOrder.LittleEndian:    // DCBA
                    bytes[0] = w2Lo; bytes[1] = w2Hi; bytes[2] = w1Lo; bytes[3] = w1Hi; break;

                case ModbusFloatByteOrder.BigEndianSwap:   // BADC (最常见的浮点格式之一)
                    bytes[0] = w1Lo; bytes[1] = w1Hi; bytes[2] = w2Lo; bytes[3] = w2Hi; break;

                case ModbusFloatByteOrder.LittleEndianSwap:// CDAB
                    bytes[0] = w2Hi; bytes[1] = w2Lo; bytes[2] = w1Hi; bytes[3] = w1Lo; break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(order));
            }

            // BitConverter.ToSingle 默认依赖当前 Windows 系统的字节序 (小端)
            // 所以我们需要把标准大端流做一次反转喂给它
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);

            return BitConverter.ToSingle(bytes, 0);
        }

        /// <summary>
        /// 【配套】：将 C# 浮点数 (float) 转换为 2 个寄存器 (ushort[2])，用于功能码 16 下发写入
        /// </summary>
        public ushort[] FloatToRegisters(float value, ModbusFloatByteOrder order)
        {
            byte[] src = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(src);

            byte a = src[0]; byte b = src[1]; byte c = src[2]; byte d = src[3];
            byte[] target = new byte[4];

            switch (order)
            {
                case ModbusFloatByteOrder.BigEndian: target[0] = a; target[1] = b; target[2] = c; target[3] = d; break;
                case ModbusFloatByteOrder.LittleEndian: target[0] = d; target[1] = c; target[2] = b; target[3] = a; break;
                case ModbusFloatByteOrder.BigEndianSwap: target[0] = b; target[1] = a; target[2] = d; target[3] = c; break;
                case ModbusFloatByteOrder.LittleEndianSwap: target[0] = c; target[1] = d; target[2] = a; target[3] = b; break;
                default: throw new ArgumentOutOfRangeException(nameof(order));
            }

            return new ushort[]
            {
        (ushort)((target[0] << 8) | target[1]),
        (ushort)((target[2] << 8) | target[3])
            };
        }
    }
}