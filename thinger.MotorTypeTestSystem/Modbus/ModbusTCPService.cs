using NModbus;          // 引用 NModbus 库，它是处理 Modbus 协议的核心
using NModbus.Device;   // 引用 Modbus Master (主站) 的定义
using System;           // C# 基础库
using System.IO;        // 用于处理 IO 异常 (比如网线拔了)
using System.Net.Sockets; // 用于 TCP 网络连接
using System.Threading; // 用于线程同步 (锁)
using System.Threading.Tasks; // 用于异步编程

namespace thinger.Mexico50Towdolly.ModbusRTU
{
    /// <summary>
    /// Modbus TCP 通讯服务类 (完整增强版)
    /// 作用：负责与 PLC 进行网络通讯，读写数据。
    /// 特点：
    /// 1. 自动重连：掉线后会自动尝试重新连接。
    /// 2. 线程安全：防止多个线程同时操作导致数据错乱。
    /// 3. 全功能：支持所有标准的 Modbus 读写指令。
    /// </summary>
    public class ModbusTCPService : IDisposable
    {
        // ========================================================================
        // 1. 私有变量 (程序内部用的工具)
        // ========================================================================

        // [标志位] 记录这个服务是否已经被销毁 (Dispose)
        // volatile 作用：保证在多线程环境下，这个变量的变化能立刻被所有线程看到
        private volatile bool _disposed = false;

        // [线程锁] 信号量 (SemaphoreSlim)。
        // Modbus 是“一问一答”的协议，不能同时问两个问题。
        // 这个锁保证同一时间只有一个线程在操作 PLC，其他人必须排队。
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);

        // [工厂] NModbus 提供的工厂类，专门用来生产 Master (主站) 对象
        private readonly ModbusFactory _factory = new ModbusFactory();

        // [TCP客户端] 底层的网络连接对象 (相当于电话线)
        private TcpClient? _client;

        // [Modbus主站] 处理协议逻辑的对象 (相当于电话机，负责把你的请求翻译成 Modbus 报文)
        private IModbusMaster? _master;

        // ========================================================================
        // 2. 公开属性 (外部可以修改的配置)
        // ========================================================================

        // [日志接口] 这是一个“委托”，允许外部把打印日志的方法传进来。
        // 外部用法：service.LogAction = (msg) => Console.WriteLine(msg);
        // 这样当发生重连或错误时，这里就会调用这个方法，把信息传出去。
        public Action<string>? LogAction { get; set; }

        // PLC 的 IP 地址 (例如 "192.168.1.5")
        public string Host { get; }

        // PLC 的端口号 (Modbus TCP 标准默认是 502)
        public int Port { get; }

        // 从站地址 (通常是 1，但也可能是别的，看 PLC 设置)
        public byte UnitId { get; set; } = 1;

        // [连接超时] 尝试连接 PLC 时，如果超过这个时间(毫秒)还没连上，就放弃
        public int ConnectTimeoutMs { get; set; } = 3000;

        // [读取超时] 发送读取指令后，如果超过这个时间没收到回复，就报错
        public int ReadTimeoutMs { get; set; } = 3000;

        // [写入超时] 发送写入指令后，如果超过这个时间没收到确认，就报错
        public int WriteTimeoutMs { get; set; } = 3000;

        // [重试次数] 如果操作失败，自动重试几次？(3代表：1次正常+2次重试)
        public int MaxAttempts { get; set; } = 3;

        // [重试间隔] 每次重试中间休息多久 (毫秒)，避免频繁轰炸 PLC
        public int RetryDelayMs { get; set; } = 1000;

        // [连接状态] 判断当前是否连着
        // 逻辑：必须 TCP 客户端不为空 + 客户端底层显示连接 + Master 对象存在
        public bool IsConnected => _client != null && _client.Connected && _master != null;

        // ========================================================================
        // 3. 构造函数 (初始化)
        // ========================================================================

        /// <summary>
        /// 创建 Modbus 服务实例
        /// </summary>
        /// <param name="host">PLC 的 IP 地址</param>
        /// <param name="port">端口号 (默认 502)</param>
        /// <param name="unitId">从站 ID (默认 1)</param>
        public ModbusTCPService(string host, int port = 502, byte unitId = 1)
        {
            // 如果传入的 host 是空的，直接报错，不允许创建
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Port = port;
            UnitId = unitId;
        }

        // ========================================================================
        // 4. 连接与断开 (基础功能)
        // ========================================================================

        /// <summary>
        /// 手动连接 PLC (异步方法)
        /// </summary>
        /// <param name="ct">取消令牌 (用于中途取消连接)</param>
        public async Task ConnectAsync(CancellationToken ct = default)
        {
            // 检查对象是否已销毁
            if (_disposed) throw new ObjectDisposedException(nameof(ModbusTCPService));

            // 1. 排队拿锁：如果有别人在用，我就在这里等
            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // 2. 如果已经连上了，就直接返回，不重复劳动
                if (IsConnected) return;

                // 3. 调用底层的连接逻辑 (ConnectCoreAsync)
                await ConnectCoreAsync(ct).ConfigureAwait(false);

                // 4. 记录日志：告诉外部连接成功了
                LogAction?.Invoke($"[Modbus] 已连接到 {Host}:{Port}");
            }
            catch (Exception ex)
            {
                // 如果连接失败，记录原因
                LogAction?.Invoke($"[Modbus] 连接失败: {ex.Message}");
                throw; // 继续把错误抛出去，让调用者知道失败了
            }
            finally
            {
                // 5. 无论成功失败，必须释放锁，把“钥匙”还回去，不然别人永远进不来
                _ioLock.Release();
            }
        }

        /// <summary>
        /// 手动断开连接 (异步方法)
        /// </summary>
        public async Task DisconnectAsync()
        {
            // 同样需要拿锁，防止在读数据读到一半的时候突然断开
            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                DisconnectCore(); // 调用底层断开逻辑
                LogAction?.Invoke($"[Modbus] 已主动断开连接");
            }
            finally
            {
                _ioLock.Release(); // 释放锁
            }
        }

        // ========================================================================
        // 5. 读写操作封装 (8大功能全集)
        // ========================================================================

        /// <summary>
        /// [功能码 01] 读取线圈 (Read Coils)
        /// 这里的“线圈”通常指 DO (输出点) 或者内部位变量，结果是 bool (开/关)
        /// </summary>
        /// <param name="startAddress">起始地址 (比如 0)</param>
        /// <param name="numberOfPoints">读取多少个 (比如 10)</param>
        public Task<bool[]> ReadCoilsAsync(ushort startAddress, ushort numberOfPoints, CancellationToken ct = default)
        {
            // 这里的 m => ... 是 Lambda 表达式，m 代表 IModbusMaster 对象
            // 意思就是：请用 ExecuteWithRetryAsync 帮我执行 m.ReadCoils 这行代码
            return ExecuteWithRetryAsync(
                action: m => m.ReadCoils(UnitId, startAddress, numberOfPoints),
                operationName: "读取线圈(01)",
                ct: ct);
        }

        /// <summary>
        /// [功能码 02] 读取离散输入 (Read Discrete Inputs)
        /// 这里的“离散输入”通常指 DI (输入点)，只读，结果是 bool
        /// </summary>
        public Task<bool[]> ReadDiscreteInputsAsync(ushort startAddress, ushort numberOfPoints, CancellationToken ct = default)
        {
            // 注意：NModbus 库里对应的方法名叫 ReadInputs
            return ExecuteWithRetryAsync(
                action: m => m.ReadInputs(UnitId, startAddress, numberOfPoints),
                operationName: "读取离散输入(02)",
                ct: ct);
        }

        /// <summary>
        /// [功能码 03] 读取保持寄存器 (Read Holding Registers)
        /// 最常用的功能！用于读取 16位 的数据 (ushort)，可读可写
        /// </summary>
        public Task<ushort[]> ReadHoldingRegistersAsync(ushort startAddress, ushort numberOfPoints, CancellationToken ct = default)
        {
            return ExecuteWithRetryAsync(
                action: m => m.ReadHoldingRegisters(UnitId, startAddress, numberOfPoints),
                operationName: "读取保持寄存器(03)",
                ct: ct);
        }

        /// <summary>
        /// [功能码 04] 读取输入寄存器 (Read Input Registers)
        /// 用于读取 16位 的数据，通常对应模拟量输入 (AI)，只读
        /// </summary>
        public Task<ushort[]> ReadInputRegistersAsync(ushort startAddress, ushort numberOfPoints, CancellationToken ct = default)
        {
            return ExecuteWithRetryAsync(
                action: m => m.ReadInputRegisters(UnitId, startAddress, numberOfPoints),
                operationName: "读取输入寄存器(04)",
                ct: ct);
        }

        /// <summary>
        /// [功能码 05] 写入单个线圈 (Write Single Coil)
        /// 开关某一个输出点 (DO)
        /// </summary>
        /// <param name="address">地址</param>
        /// <param name="value">值 (true=开, false=关)</param>
        public Task WriteSingleCoilAsync(ushort address, bool value, CancellationToken ct = default)
        {
            // 这里的 return true 是为了凑数，因为 ExecuteWithRetryAsync 需要一个返回值
            return ExecuteWithRetryAsync(
                action: m => { m.WriteSingleCoil(UnitId, address, value); return true; },
                operationName: "写入单线圈(05)",
                ct: ct);
        }

        /// <summary>
        /// [功能码 06] 写入单个寄存器 (Write Single Register)
        /// 修改某一个数值
        /// </summary>
        /// <param name="address">地址</param>
        /// <param name="value">值 (0-65535)</param>
        public Task WriteSingleRegisterAsync(ushort address, ushort value, CancellationToken ct = default)
        {
            return ExecuteWithRetryAsync(
                action: m => { m.WriteSingleRegister(UnitId, address, value); return true; },
                operationName: "写入单寄存器(06)",
                ct: ct);
        }

        /// <summary>
        /// [功能码 15] 写入多个线圈 (Write Multiple Coils)
        /// 批量控制一组开关
        /// </summary>
        /// <param name="startAddress">起始地址</param>
        /// <param name="data">布尔数组</param>
        public Task WriteMultipleCoilsAsync(ushort startAddress, bool[] data, CancellationToken ct = default)
        {
            return ExecuteWithRetryAsync(
                action: m => { m.WriteMultipleCoils(UnitId, startAddress, data); return true; },
                operationName: "写入多线圈(15)",
                ct: ct);
        }

        /// <summary>
        /// [功能码 10] 写入多个寄存器 (Write Multiple Registers)
        /// 批量修改一组数值
        /// </summary>
        /// <param name="startAddress">起始地址</param>
        /// <param name="data">数据数组</param>
        public Task WriteMultipleRegistersAsync(ushort startAddress, ushort[] data, CancellationToken ct = default)
        {
            return ExecuteWithRetryAsync(
                action: m => { m.WriteMultipleRegisters(UnitId, startAddress, data); return true; },
                operationName: "写入多寄存器(10)",
                ct: ct);
        }


        // ========================================================================
        // 6. 核心执行引擎 (所有读写都走这里，包含自动重连逻辑)
        // ========================================================================

        /// <summary>
        /// 通用的执行方法：负责 处理锁、重试、自动重连、异常日志
        /// </summary>
        /// <typeparam name="T">返回值的类型 (比如 bool[] 或 ushort[])</typeparam>
        /// <param name="action">具体要执行的操作 (比如读取电压)</param>
        /// <param name="operationName">操作名称 (用于写日志，比如"读取保持寄存器")</param>
        /// <param name="ct">取消令牌</param>
        private async Task<T> ExecuteWithRetryAsync<T>(Func<IModbusMaster, T> action, string operationName, CancellationToken ct)
        {
            // 1. 安全检查：如果对象已销毁，直接报错
            if (_disposed) throw new ObjectDisposedException(nameof(ModbusTCPService));

            // 2. 获取锁：保证同一时间只有一个线程能执行下面的代码
            await _ioLock.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                Exception lastException = null; // 用来存最后一次失败的原因

                // 3. 开始循环重试 (attempt = 第几次尝试)
                // 假设 MaxAttempts = 3，那么会尝试：第1次 -> 失败 -> 第2次 -> 失败 -> 第3次
                for (int attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    // 检查是否有人按了“取消”
                    if (ct.IsCancellationRequested) throw new TaskCanceledException();

                    try
                    {
                        // 4. 检查连接状态
                        // 如果是第2次及以上尝试 (attempt > 1)，说明刚才失败了，必须重连。
                        // 或者如果当前 IsConnected 是 false，也必须先连。
                        if (attempt > 1 || !IsConnected)
                        {
                            if (attempt > 1) // 只有重试的时候才记日志，第一次如果不连会自动连，不用废话
                                LogAction?.Invoke($"[Modbus] {operationName} 失败，正在尝试第 {attempt} 次重连...");

                            // 执行重连 (先断开，再连接)
                            await ReconnectCoreAsync(ct).ConfigureAwait(false);
                        }

                        // 5. [最关键的一步] 执行真正的 Modbus 操作
                        // 我们用 Task.Run 把它放到后台线程去跑
                        // 为什么要这样？因为 NModbus 库的读写是同步的，如果不放后台，界面会卡住
                        return await Task.Run(() => action(_master!), ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (IsConnectionLikeException(ex))
                    {
                        // 6. 捕获到了“网络断开”或“超时”类的异常
                        lastException = ex;
                        LogAction?.Invoke($"[Modbus警告] {operationName} 发生异常: {ex.Message}");

                        // 如果这已经是最后一次尝试了，就不等了，直接跳出循环
                        if (attempt == MaxAttempts) break;

                        // 7. 等待一段时间再试 (让网络喘口气)
                        try
                        {
                            await Task.Delay(RetryDelayMs, ct).ConfigureAwait(false);
                        }
                        catch (TaskCanceledException)
                        {
                            break; // 等待期间被取消
                        }
                    }
                    catch (Exception ex)
                    {
                        // 8. 捕获到了“其他异常” (比如参数错误)
                        // 这种错误重连也没用，直接记录日志并抛出，不再重试
                        LogAction?.Invoke($"[Modbus错误] {operationName} 发生不可恢复错误: {ex.Message}");
                        throw;
                    }
                }

                // 9. 如果循环结束了还没返回，说明彻底失败了
                LogAction?.Invoke($"[Modbus故障] {operationName} 在尝试 {MaxAttempts} 次后彻底失败。");

                // 抛出一个 IO 异常，告诉上层调用者彻底挂了
                throw new IOException($"Modbus操作失败: {lastException?.Message}", lastException);
            }
            finally
            {
                // 10. 无论如何，最后必须释放锁！否则程序会死锁。
                _ioLock.Release();
            }
        }

        // ========================================================================
        // 7. 底层网络辅助方法 (处理 TCP 连接的细节)
        // ========================================================================

        /// <summary>
        /// [底层] 建立 TCP 连接并创建 ModbusMaster
        /// 注意：这个方法内部不加锁，因为它是被 ExecuteWithRetryAsync (已经加了锁) 调用的
        /// </summary>
        private async Task ConnectCoreAsync(CancellationToken ct)
        {
            // 先清理旧的连接，以防万一
            DisconnectCore();

            // 创建一个新的 TCP 客户端
            var client = new TcpClient
            {
                NoDelay = true, // 禁用 Nagle 算法，让数据包立即发送，减少延迟，对实时性很重要
                ReceiveTimeout = ReadTimeoutMs, // 设置底层接收超时
                SendTimeout = WriteTimeoutMs    // 设置底层发送超时
            };

            try
            {
                // [处理连接超时]
                // C# 原生的 ConnectAsync 居然不支持设置超时时间，所以我们需要自己用 Task.WhenAny 变通实现
                var connectTask = client.ConnectAsync(Host, Port);
                var timeoutTask = Task.Delay(ConnectTimeoutMs, ct); // 创建一个定时器

                // 等待 连接任务 和 定时器任务，看谁先完成
                var completed = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);

                // 3. 如果超时了（timeoutTask 赢了）
                if (completed != connectTask)
                {                    
                    // 我们知道 connectTask 待会肯定会报错（因为我们要关连接了）。
                    // 所以给它挂一个“后续任务”：
                    _ = connectTask.ContinueWith(t =>
                    {
                        // 当 connectTask 报错结束时，会进入这里。
                        if (t.IsFaulted)
                        {
                            // 我们只要访问一下 t.Exception，
                            // .NET 就会认为：“哦，程序员已经知道这个错误了”。
                            // 这样就不会算作“未观察到的异常”，也不会报错给系统了。
                            var ex = t.Exception;
                        }
                    }, TaskContinuationOptions.OnlyOnFaulted); // 仅在报错时执行
                                                             
                    // 4. 现在可以放心地关连接了
                    // 即使 connectTask 待会报错，也会被上面的 ContinueWith 捕获，不会炸到外面去
                    try { client.Close(); } catch { }

                    // 5. 抛出超时异常给界面显示
                    throw new TimeoutException($"连接 {Host}:{Port} 超时");
                }

                // 即使连接任务完成了，也可能是有异常（比如被拒绝），所以要 await 一下捕获错误
                await connectTask.ConfigureAwait(false);

                // 连接成功！创建 Modbus Master 协议处理器
                var master = _factory.CreateMaster(client);

                // [重要配置] 禁用 NModbus 内部的重试
                // 原因：我们已经在最外层写了更高级的重试循环。
                // 如果这里不设为0，就会出现“外层重试3次 * 内层重试3次 = 9次”的情况，导致程序假死很久。
                master.Transport.Retries = 0;
                master.Transport.ReadTimeout = ReadTimeoutMs;
                master.Transport.WriteTimeout = WriteTimeoutMs;
                master.Transport.WaitToRetryMilliseconds = 50;

                // 保存实例，标记为已连接
                _client = client;
                _master = master;
            }
            catch
            {
                // 如果中间任何一步出错，确保 client 被关闭，防止资源泄漏
                try { client.Close(); } catch { }
                throw;
            }
        }

        /// <summary>
        /// [底层] 重新连接：先断开 -> 再连接
        /// </summary>
        private async Task ReconnectCoreAsync(CancellationToken ct)
        {
            DisconnectCore(); // 拔网线
            await ConnectCoreAsync(ct); // 插网线
        }

        /// <summary>
        /// [底层] 清理连接资源
        /// </summary>
        private void DisconnectCore()
        {
            // 1. 释放 Modbus Master
            if (_master is IDisposable m)
            {
                try { m.Dispose(); } catch { }
            }
            _master = null;

            // 2. 关闭 TCP 客户端
            if (_client != null)
            {
                try { _client.Close(); } catch { } // Close 内部会释放资源
            }
            _client = null;
        }

        /// <summary>
        /// [辅助] 判断异常是否属于“网络连接问题”
        /// 只有这些异常我们才应该重试，如果是代码逻辑错误就不应该重试
        /// </summary>
        private static bool IsConnectionLikeException(Exception ex)
        {
            return ex is IOException            // IO 错误 (通常是网线拔了)
                || ex is SocketException        // Socket 错误 (连接被对方复位)
                || ex is TimeoutException       // 超时
                || ex is ObjectDisposedException // 对象已被关闭
                || ex is InvalidOperationException // 操作无效 (NModbus 有时会抛这个)
                                                   // 递归检查内部异常 (有时候真正的错误是被包裹在里面的)
                || (ex.InnerException != null && IsConnectionLikeException(ex.InnerException));
        }

        // ========================================================================
        // 8. 浮点数解析辅助 (工具方法)
        // PLC 的寄存器是 16 位的 (ushort)，而 float 是 32 位的。
        // 所以 1 个 float 需要占用 2 个寄存器。下面这些方法负责拼接数据。
        // ========================================================================
        //BigEndian（标准大端序） = ABCD, LittleEndian（标准小端序） = DCBA ,BigEndianSwap（大端序字节交换） = BADC ,LittleEndianSwap（小端序字交换） = CDAB
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

        // ========================================================================
        // 9. 销毁逻辑 (Dispose 模式)
        // ========================================================================

        /// <summary>
        /// 销毁对象，释放所有资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return; // 如果已经销毁过，就不再执行
            _disposed = true;      // 标记为已销毁

            // 尝试等待最多 200ms 让当前的读写任务完成，避免直接杀掉线程导致错误
            try { _ioLock.Wait(200); } catch { }

            // 执行断开操作
            DisconnectCore();

            // 销毁锁对象
            _ioLock.Dispose();
        }
    }
}