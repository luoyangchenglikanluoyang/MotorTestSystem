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

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            
            // 开启后台独立线程，死循环采集
            Task.Run(() => PollingLoop(_cts.Token));
            _logger.Information("数据采集引擎已启动");
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            _logger.Information("数据采集引擎已停止");
        }

        private async Task PollingLoop(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    foreach (var group in _groups)
                    {
                        // 1. 根据 DeviceId 找到底层的设备配置
                        var deviceConfig = _deviceManager.DeviceConfigs.Find(d => d.DeviceId == group.DeviceId);
                        if (deviceConfig == null) continue;

                        try
                        {
                            // 2. 从全局管家拿到modbusRTU或者modbusTCP的Service服务 (不增加 UserCount)
                            object service = ConnectionManager.PeekService(deviceConfig);
                            if (service == null) continue; // 如果前台还没连上，这里就是 null，直接跳过等下一轮

                            // 3. 判断并发送成块读取指令
                            if (service is ModbusTCPService tcp && tcp.IsConnected)
                            {
                                if (group.FunctionCode == 1)
                                {
                                    bool[] boolData = await tcp.ReadCoilsAsync(group.StartAddress, group.Length, ct);
                                    if (boolData != null && boolData.Length >= group.Length) ParseGroupDataBool(group, boolData);
                                }
                                else if (group.FunctionCode == 2)
                                {
                                    bool[] boolData = await tcp.ReadDiscreteInputsAsync(group.StartAddress, group.Length, ct);
                                    if (boolData != null && boolData.Length >= group.Length) ParseGroupDataBool(group, boolData);
                                }
                                else if (group.FunctionCode == 3)
                                {
                                    ushort[] rawData = await tcp.ReadHoldingRegistersAsync(group.StartAddress, group.Length, ct);
                                    if (rawData != null && rawData.Length >= group.Length) ParseGroupData(group, rawData, service);
                                }
                                else if (group.FunctionCode == 4)
                                {
                                    ushort[] rawData = await tcp.ReadInputRegistersAsync(group.StartAddress, group.Length, ct);
                                    if (rawData != null && rawData.Length >= group.Length) ParseGroupData(group, rawData, service);
                                }
                            }
                            else if (service is ModbusRTUService rtu && rtu.IsConnected)
                            {
                                if (group.FunctionCode == 1)
                                {
                                    bool[] boolData = await rtu.ReadCoilsAsync(deviceConfig.SlaveId, group.StartAddress, group.Length, ct);
                                    if (boolData != null && boolData.Length >= group.Length) ParseGroupDataBool(group, boolData);
                                }
                                else if (group.FunctionCode == 2)
                                {
                                    bool[] boolData = await rtu.ReadDiscreteInputsAsync(deviceConfig.SlaveId, group.StartAddress, group.Length, ct);
                                    if (boolData != null && boolData.Length >= group.Length) ParseGroupDataBool(group, boolData);
                                }
                                else if (group.FunctionCode == 3)
                                {
                                    ushort[] rawData = await rtu.ReadHoldingRegistersAsync(deviceConfig.SlaveId, group.StartAddress, group.Length, ct);
                                    if (rawData != null && rawData.Length >= group.Length) ParseGroupData(group, rawData, service);
                                }
                                else if (group.FunctionCode == 4)
                                {
                                    ushort[] rawData = await rtu.ReadInputRegistersAsync(deviceConfig.SlaveId, group.StartAddress, group.Length, ct);
                                    if (rawData != null && rawData.Length >= group.Length) ParseGroupData(group, rawData, service);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // 如果断线，底层的通讯库会抛出异常。
                            // 我们在这里安静地捕获，这轮就不切蛋糕了，等下一轮重试。
                            _logger.Debug($"读取组 {group.GroupName} 失败: {ex.Message}");
                        }
                    }

                    // 休息 500ms 继续下一轮 (这就是轮询周期，可以根据需要改快点或慢点)
                    await Task.Delay(500, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消，吞掉异常，防止后台任务报错
            }
        }

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
}
