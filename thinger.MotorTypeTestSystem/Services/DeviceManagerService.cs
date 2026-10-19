using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using thinger.MotorTypeTestSystem.Models.Hardware;

namespace thinger.MotorTypeTestSystem.Services
{
    /// <summary>
    /// 设备通讯列表服务，用来解析设备的json文件，并且如果修改了设备的端口号或者IP等信息可以进行保存
    /// </summary>
    public class DeviceManagerService : IDeviceManagerService
    {
        private readonly string _configFilePath;
        private readonly ILogger _logger;

        public List<DeviceConfig> DeviceConfigs { get; private set; } = new List<DeviceConfig>();

        // 构造函数：只接依赖、算路径，几微秒内必须瞬间完成
        public DeviceManagerService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // 纯内存字符串拼接，绝不碰磁盘（耗时操作）
            // AppDomain.CurrentDomain.BaseDirectory获取当前程序运行所在的。 基准目录（即你的 文件所在的文件路径）.exe
            // Path.Combine来自 命名空间，专门用于System.IO安全拼接文件路径。
            _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "devices.json");
        }

        public void InitializeConfigs()
        {
            try
            {
                if (File.Exists(_configFilePath))//Exists是存在性检查方法，查看一下_configFilePath这个文件是否存在
                {
                    string json = File.ReadAllText(_configFilePath);
                    DeviceConfigs = JsonSerializer.Deserialize<List<DeviceConfig>>(json) ?? new List<DeviceConfig>();//??的意思是假如
                                                                                                                     //JsonSerializer.Deserialize<List<DeviceConfig>>(json)不为空则返回反序列化后的列表，
                                                                                                                     //如果为空则返回一个新的列表
                    _logger.Information("成功加载设备配置 {FilePath}，共 {Count} 台设备", _configFilePath, DeviceConfigs.Count);
                }
                else
                {
                    _logger.Warning("未找到设备配置文件，正在生成默认模板配置...");
                    DeviceConfigs = new List<DeviceConfig>
                    {
                        new DeviceConfig { DeviceId = "PLC_1", DeviceName = "主控PLC", CommType = CommunicationType.ModbusTCP, IpAddress = "192.168.0.10", Port = 502 },
                        new DeviceConfig { DeviceId = "Meter_1", DeviceName = "功率分析仪", CommType = CommunicationType.ModbusRTU, ComPort = "COM1", BaudRate = 9600 }
                    };
                    SaveConfigs(); // 文件不存在时保存默认模板
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "加载设备配置文件失败！");
                DeviceConfigs = new List<DeviceConfig>();
            }
        }

        public void SaveConfigs()
        {
            try
            {
                // 保存前做目录检查，谁用磁盘谁负责确保环境完备
                //string?这是可空引用类型标记，表示 dir 变量可能是 null。
                //path是一个静态工具类，专门用来处理“路径字符串”。它不涉及硬盘读写，只做字符串的切割、拼接、提取。
                //GetDirectoryName它的唯一功能是：输入一个“文件完整路径”，输出这个文件所在的“文件夹完整路径”。
                string? dir = Path.GetDirectoryName(_configFilePath);

                // 判断：如果目录字符串有效，并且当前磁盘上根本没有这个文件夹
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))//Directory专门处理文件夹的创建、删除、检查
                {
                    // 主动在磁盘上创建出这个文件夹（解决首次运行文件夹不存在的问题）
                    Directory.CreateDirectory(dir);
                }

                // 配置序列化参数
                // WriteIndented = true 表示生成的 JSON 带缩进和换行，不是挤成一行，方便人类肉眼用记事本修改
                var options = new JsonSerializerOptions { WriteIndented = true };
                // 将 C# 的 List<DeviceConfig> 转成带优美格式的 JSON 字符串
                string json = JsonSerializer.Serialize(DeviceConfigs, options);
                // 将文本写入磁盘覆盖保存
                File.WriteAllText(_configFilePath, json);
                _logger.Information("设备配置已成功持久化至本地文件");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "保存设备配置文件失败！");
            }
        }
    }
}