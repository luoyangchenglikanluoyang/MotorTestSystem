using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using thinger.Mexico50Towdolly.ModbusRTU;

namespace thinger.MotorTypeTestSystem.Services
{
    public class ResistanceMeterService : IResistanceMeterService
    {
        private readonly IDeviceManagerService _deviceManager;
        private readonly ILogger _logger;

        public ResistanceMeterService(IDeviceManagerService deviceManager, ILogger logger)
        {
            _deviceManager = deviceManager;
            _logger = logger;
        }

        public async Task<(bool Success, double Resistance, string Message)> ReadResistanceAsync()
        {
            try
            {
                // 1. 从设备管理器拿到电桥设备的配置
                var bridgeConfig = _deviceManager.DeviceConfigs.Find(d => d.DeviceName == "直流电阻电桥");
                if (bridgeConfig == null)
                {
                    return (false, 0, "未找到电桥设备配置 (直流电阻仪)，请检查 devices.json！");
                }
                // 2. 拿到电桥的当前通信服务实例
                object service = ConnectionManager.PeekService(bridgeConfig);
                if (service == null)
                {
                    return (false, 0, "电桥尚未连接，请先在【通信诊断与配置】中连接电桥！");
                }
                double currentVal = 0;
                // 3. 支持 Modbus RTU 串口通信
                if (service is ModbusRTUService rtu && rtu.IsConnected)
                {
                    ushort[] raw = await rtu.ReadHoldingRegistersAsync(bridgeConfig.SlaveId, 0, 2);
                    if (raw != null && raw.Length >= 2)
                    {
                        if (CustomModbusParsers.Parsers.TryGetValue("电桥_毫欧转欧姆", out var parseFunc))
                        {
                            currentVal = Convert.ToDouble(parseFunc(raw, 0));
                        }
                    }
                }
                // 4. 支持 Modbus TCP 网口通信
                else if (service is ModbusTCPService tcp && tcp.IsConnected)
                {
                    ushort[] raw = await tcp.ReadHoldingRegistersAsync(0, 2);
                    if (raw != null && raw.Length >= 2)
                    {
                        currentVal = tcp.RegistersToFloat(raw, 0, ModbusTCPService.ModbusFloatByteOrder.BigEndian);
                    }
                }
                else
                {
                    return (false, 0, "电桥通信链路处于断开状态，请检查连线！");
                }
                // 5. 校验阻值合理性
                if (currentVal <= 0)
                {
                    return (false, 0, "采集到的电阻值为 0 或未读取到有效数值，请检查接线！");
                }
                // 保留 5 位小数
                currentVal = Math.Round(currentVal, 5);
                return (true, currentVal, "读取成功");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "微欧计读取电阻时发生异常");
                return (false, 0, $"读取异常：{ex.Message}");
            }
        }
    }
}
