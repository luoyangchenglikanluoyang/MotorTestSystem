using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using thinger.Mexico50Towdolly.ModbusRTU;
using thinger.MotorTypeTestSystem.Models.Hardware;

namespace thinger.MotorTypeTestSystem.Services
{
    public interface IDataAcquisitionService
    {
        void Start();
        void Stop();
    }

    public class DataAcquisitionService : IDataAcquisitionService
    {
        private readonly IDeviceManagerService _deviceManager;//接受注入的设备接口
        private readonly TagPoolService _tagPool;//接受全局变量池
        private readonly ILogger _logger;

        private List<ModbusGroupConfig> _groups = new();//json文件解析设备的数据点位
        private CancellationTokenSource _cts;//取消令牌的源

        public DataAcquisitionService(IDeviceManagerService deviceManager, TagPoolService tagPool, ILogger logger)
        {
            _deviceManager = deviceManager;
            _tagPool = tagPool;
            _logger = logger;
            LoadConfig();
        }

        #region 加载点表配置（反序列化json文件）
        /// <summary>
        /// 加载点表配置
        /// </summary>
        private void LoadConfig()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "tags.json");//path其实就是路径工具箱
                //Path.Combine 确实是用来“拼接”路径字符串的。 但它不是简单的 + 号拼接，而是会智能处理路径分隔符，避免你手动写 \ 或 / 时出错。
                //Path 是 .NET 里 System.IO 命名空间下的一个静态类，专门用来处理文件路径字符串。它不读文件、不写文件，只负责对路径进行“字符串操作”
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    _groups = JsonSerializer.Deserialize<List<ModbusGroupConfig>>(json) ?? new List<ModbusGroupConfig>();
                    _logger.Information($"成功加载点表配置，共 {_groups.Count} 个组。");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "加载 tags.json 失败");
            }
        }
        #endregion

        #region 启动数据采集
        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();

            var channelGroups = _groups.GroupBy(g =>
            {
                var dev = _deviceManager.DeviceConfigs.Find(d => d.DeviceId == g.DeviceId);
                if (dev == null) return "UNKNOWN";
                // 网口按 IP:Port 分组，串口按 COM口 分组
                return dev.CommType == CommunicationType.ModbusTCP
                    ? $"TCP_{dev.IpAddress}:{dev.Port}"
                    : $"RTU_{dev.ComPort}";
            })
            .Where(g => g.Key != "UNKNOWN");//过滤掉刚才没找到设备的那些通道组
            // 2. 为每个物理端口启动一个独立的后台任务（互不干扰、永不卡死）
            foreach (var channelGroup in channelGroups)
            {
                string channelKey = channelGroup.Key;
                var groupList = channelGroup.ToList();
                Task.Run(() => PollingChannelLoop(channelKey, groupList, _cts.Token));
                _logger.Information($"已为物理链路 [{channelKey}] 启动独立采集任务，包含 {groupList.Count} 个点位组。");
            }
            _logger.Information("多通道并发数据采集引擎已全部启动！");
        }
        #endregion

        #region 关闭数据采集
        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            _logger.Information("数据采集引擎已停止");
        }
        #endregion


        /// <summary>
        /// 专属物理链路的采集循环 (不同端口完全并行，相同端口内部排队)
        /// </summary>
        private async Task PollingChannelLoop(string channelKey, List<ModbusGroupConfig> channelGroups, CancellationToken ct)
        {


            // 1. 遍历传进来的所有组，为每个组生成一张卡片
            var groupStates = channelGroups.Select(g =>
            {
                // 去总设备表里找到它对应的设备配置（拿到从站地址 SlaveId）
                var deviceConfig = _deviceManager.DeviceConfigs.Find(d => d.DeviceId == g.DeviceId);
                return new PollingState
                {
                    Group = g,
                    DeviceConfig = deviceConfig,
                    ScanRateMs = deviceConfig?.ScanRateMs ?? 500, // 拿到这个设备设定的周期（默认500ms）
                    NextRunTime = DateTime.MinValue // 【关键】设为极小值（公元1年1月1日）
                };
            })
            .Where(s => s.DeviceConfig != null) // 如果配置里没找到这个设备，就过滤扔掉，防止报错
            .ToList();

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    DateTime now = DateTime.UtcNow;// 抓取当前世界标准时间
                    bool executedAny = false;

                    foreach (var state in groupStates)
                    {
                        // 关键判断：当前时间到期了吗？
                        if (now >= state.NextRunTime)
                        {
                            try
                            {
                                object service = ConnectionManager.PeekService(state.DeviceConfig);
                                if (service == null) continue;

                                // --- 下面是你原有的 Modbus 读写逻辑，保持不变 ---
                                if (service is ModbusTCPService tcp && tcp.IsConnected)
                                {
                                    if (state.Group.FunctionCode == 3)
                                    {
                                        ushort[] rawData = await tcp.ReadHoldingRegistersAsync(state.Group.StartAddress, state.Group.Length, ct);
                                        if (rawData != null && rawData.Length >= state.Group.Length)
                                        {
                                            ParseGroupData(state.Group, rawData, service);
                                            ComputeDerivedTags();
                                        }
                                    }
                                    else if (state.Group.FunctionCode == 4)
                                    {
                                        ushort[] rawData = await tcp.ReadInputRegistersAsync(state.Group.StartAddress, state.Group.Length, ct);
                                        if (rawData != null && rawData.Length >= state.Group.Length)
                                        {
                                            ParseGroupData(state.Group, rawData, service);
                                            ComputeDerivedTags();
                                        }
                                    }
                                    else if (state.Group.FunctionCode == 1 || state.Group.FunctionCode == 2)
                                    {
                                        bool[] boolData = await tcp.ReadCoilsAsync(state.Group.StartAddress, state.Group.Length, ct);
                                        if (boolData != null && boolData.Length >= state.Group.Length) ParseGroupDataBool(state.Group, boolData);
                                    }
                                }
                                else if (service is ModbusRTUService rtu && rtu.IsConnected)
                                {
                                    if (state.Group.FunctionCode == 3)
                                    {
                                        ushort[] rawData = await rtu.ReadHoldingRegistersAsync(state.DeviceConfig.SlaveId, state.Group.StartAddress, state.Group.Length, ct);
                                        if (rawData != null && rawData.Length >= state.Group.Length)
                                        {
                                            ParseGroupData(state.Group, rawData, service);
                                            ComputeDerivedTags();
                                        }
                                    }
                                    else if (state.Group.FunctionCode == 4)
                                    {
                                        ushort[] rawData = await rtu.ReadInputRegistersAsync(state.DeviceConfig.SlaveId, state.Group.StartAddress, state.Group.Length, ct);
                                        if (rawData != null && rawData.Length >= state.Group.Length)
                                        {
                                            ParseGroupData(state.Group, rawData, service);
                                            ComputeDerivedTags();
                                        }
                                    }
                                    else if (state.Group.FunctionCode == 1 || state.Group.FunctionCode == 2)
                                    {
                                        bool[] boolData = await rtu.ReadCoilsAsync(state.DeviceConfig.SlaveId, state.Group.StartAddress, state.Group.Length, ct);
                                        if (boolData != null && boolData.Length >= state.Group.Length) ParseGroupDataBool(state.Group, boolData);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Debug($"链路 [{channelKey}] 读取设备 {state.Group.DeviceId} 失败: {ex.Message}");
                            }
                            finally
                            {
                                // 3. 关键点：无论成功失败，读取完成后，更新该组的下一次执行时间
                                // 这里使用 DateTime.UtcNow 加周期，而不是之前的 now，
                                // 是因为如果网络卡顿导致读取花了很久，从完成时刻往后推周期最安全，能防止“拥堵累积”
                                state.NextRunTime = DateTime.UtcNow.AddMilliseconds(state.ScanRateMs);
                                executedAny = true;
                            }
                        }
                    }

                    // 动态休眠防止 CPU 跑满 100%
                    await Task.Delay(executedAny ? 1 : 20, ct);
                }
            }
            catch (OperationCanceledException) { }
        }

        #region 自动计算“虚拟/派生测点”，并持续刷新存入全局变量池
        /// <summary>
        /// 自动计算“虚拟/派生测点”，并持续刷新存入全局变量池
        /// </summary>
        /// <summary>
        /// 硬件数据刚写入变量池的一瞬间，立刻就地执行派生公式刷新
        /// </summary>
        private void ComputeDerivedTags()
        {
            try
            {
                // -------------------------------------------------------------
                // 1. 三相平均电压 U_avg (V) 与 平均电流 I_avg (A)
                // -------------------------------------------------------------
                double uab = _tagPool.GetValue<float>("IO.Voltage_Uab");
                double ubc = _tagPool.GetValue<float>("IO.Voltage_Ubc");
                double uca = _tagPool.GetValue<float>("IO.Voltage_Uca");
                if (uab > 0 || ubc > 0 || uca > 0)
                {
                    _tagPool.SetValue("U_avg", Math.Round((uab + ubc + uca) / 3.0, 1));
                }

                double ia = _tagPool.GetValue<float>("IO.Current_Ia");
                double ib = _tagPool.GetValue<float>("IO.Current_Ib");
                double ic = _tagPool.GetValue<float>("IO.Current_Ic");
                if (ia > 0 || ib > 0 || ic > 0)
                {
                    _tagPool.SetValue("I_avg", Math.Round((ia + ib + ic) / 3.0, 2));
                }

                // -------------------------------------------------------------
                // 2. 机械输出功率 P2 (kW)：P2 = (转矩 T * 转速 n) / 9550
                // -------------------------------------------------------------
                double speed = _tagPool.GetValue<ushort>("IO.PLC_Speed");
                double torque = _tagPool.GetValue<float>("IO.PLC_Torque");
                if (speed > 0 && torque > 0)
                {
                    double p2 = Math.Round((torque * speed) / 9550.0, 2);
                    _tagPool.SetValue("P2_Output", p2);
                }

                // -------------------------------------------------------------
                // 3. 实时运行效率 η (%)：η = (P2 / P1) * 100
                // -------------------------------------------------------------
                double p1 = _tagPool.GetValue<float>("P_active");
                double currentP2 = _tagPool.GetValue<double>("P2_Output");
                if (p1 > 0 && currentP2 > 0)
                {
                    _tagPool.SetValue("Motor_Efficiency", Math.Round((currentP2 / p1) * 100.0, 1));
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"就地计算派生数据异常: {ex.Message}");
            }
        }
        #endregion

        private void ParseGroupData(ModbusGroupConfig group, ushort[] rawData, object service)
        {
            foreach (var tag in group.Tags)
            {
                // 安全防呆：防止偏移量写错导致越界崩溃
                if (tag.Offset >= rawData.Length) continue;

                if (tag.DataType == "Float" && tag.Offset + 1 < rawData.Length)
                {
                    // 获取当前点位的字节序，如果没有配置则默认 ABCD,IsNullOrEmpty是判断是否为空，如果为空则默认ABCD，否则就将tag.ByteOrder赋值给currentOrder
                    string currentOrder = string.IsNullOrEmpty(tag.ByteOrder) ? "ABCD" : tag.ByteOrder;

                    // 调用通讯库里自带的拼接方法 (复用你写的底层代码)
                    float val = 0;
                    if (service is ModbusTCPService tcp)
                    {
                        var tcpOrder = currentOrder switch
                        {
                            "DCBA" => ModbusTCPService.ModbusFloatByteOrder.LittleEndian,
                            "BADC" => ModbusTCPService.ModbusFloatByteOrder.BigEndianSwap,
                            "CDAB" => ModbusTCPService.ModbusFloatByteOrder.LittleEndianSwap,
                            _ => ModbusTCPService.ModbusFloatByteOrder.BigEndian//_是如果上面三个都没有的话就默认用这个，就算填错了也是用这个
                        };
                        val = tcp.RegistersToFloat(rawData, tag.Offset, tcpOrder);
                    }
                    else if (service is ModbusRTUService rtu)
                    {
                        var rtuOrder = currentOrder switch
                        {
                            "DCBA" => ModbusRTUService.ModbusFloatByteOrder.LittleEndian,
                            "BADC" => ModbusRTUService.ModbusFloatByteOrder.BigEndianSwap,
                            "CDAB" => ModbusRTUService.ModbusFloatByteOrder.LittleEndianSwap,
                            _ => ModbusRTUService.ModbusFloatByteOrder.BigEndian
                        };
                        val = rtu.RegistersToFloat(rawData, tag.Offset, rtuOrder);
                    }

                    _tagPool.SetValue(tag.TagName, val);
                }
                else if (tag.DataType == "UShort")
                {
                    ushort val = rawData[tag.Offset];
                    _tagPool.SetValue(tag.TagName, val);
                }
                else if (tag.DataType == "Bool")
                {
                    // 取第0位 (以后你可以把它改成根据 tag.BitIndex 提取特定位)
                    bool val = (rawData[tag.Offset] & (1 << tag.BitIndex)) != 0;
                    _tagPool.SetValue(tag.TagName, val);
                }
                else if (tag.DataType == "Custom")
                {
                    // 【终极武器】：去解药库里找对应的方法
                    if (CustomModbusParsers.Parsers.TryGetValue(tag.CustomMethod, out var parseAction))
                    {
                        // 把原始大数组和偏移量扔给它，让它自己去算
                        object finalValue = parseAction(rawData, tag.Offset);
                        _tagPool.SetValue(tag.TagName, finalValue);
                    }
                }
            }
        }

        private void ParseGroupDataBool(ModbusGroupConfig group, bool[] rawData)
        {
            foreach (var tag in group.Tags)
            {
                if (tag.Offset >= rawData.Length) continue;

                if (tag.DataType == "Bool")
                {
                    bool val = rawData[tag.Offset];
                    _tagPool.SetValue(tag.TagName, val);
                }
            }
        }
    }

    /// <summary>
    /// 通信块状态辅助卡片
    /// </summary>
    public class PollingState
    {
        public ModbusGroupConfig Group { get; set; }    //数据块组
        public DeviceConfig DeviceConfig { get; set; }  //设备组 从站站号是多少？属于哪个设备？
        public int ScanRateMs { get; set; }             // 这组数据的采集周期（毫秒）
        public DateTime NextRunTime { get; set; }       // 下一次该读它的具体时间
    }
}
