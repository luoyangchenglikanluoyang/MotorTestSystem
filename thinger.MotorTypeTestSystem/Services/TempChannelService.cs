using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using thinger.MotorTypeTestSystem.Models;

namespace thinger.MotorTypeTestSystem.Services
{
    /// <summary>
    /// 全局测温通道服务实现类（单例）
    /// 统一管理 8 路巡检仪通道的映射关系、测点名称与启用状态
    /// </summary>
    public class TempChannelService : ITempChannelService
    {
        private readonly ILogger _logger;
        private readonly string _configFilePath;

        public ObservableCollection<TempChannelConfig> Channels { get; } = new();

        public event Action? ChannelsChanged;

        public TempChannelService(ILogger logger)
        {
            _logger = logger;
            _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "temp_channels.json");
            LoadConfig();
        }

        public void LoadConfig()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    var list = JsonSerializer.Deserialize<List<TempChannelConfig>>(json);
                    if (list != null && list.Count > 0)
                    {
                        Channels.Clear();
                        foreach (var item in list)
                        {
                            Channels.Add(item);
                        }
                        _logger.Information($"成功从本地加载 {Channels.Count} 个测温通道配置。");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "加载测温通道配置文件失败，正在回滚至出厂默认配置...");
            }

            // 若文件不存在或反序列化失败，初始化 8 路标准默认通道
            InitDefaultChannels();
            SaveConfig();
        }

        private void InitDefaultChannels()
        {
            Channels.Clear();
            Channels.Add(new TempChannelConfig
            {
                ChannelIndex = 1,
                TagName = "Temp.CH1",
                PointName = "环境温度",
                IsEnabled = true,
                PointType = "环境基准",
                IsAmbient = true
            });
            Channels.Add(new TempChannelConfig
            {
                ChannelIndex = 2,
                TagName = "Temp.CH2",
                PointName = "机壳表面",
                IsEnabled = true,
                PointType = "结构件",
                IsAmbient = false
            });
            Channels.Add(new TempChannelConfig
            {
                ChannelIndex = 3,
                TagName = "Temp.CH3",
                PointName = "前轴承",
                IsEnabled = true,
                PointType = "轴承",
                IsAmbient = false
            });
            Channels.Add(new TempChannelConfig
            {
                ChannelIndex = 4,
                TagName = "Temp.CH4",
                PointName = "后轴承",
                IsEnabled = true,
                PointType = "轴承",
                IsAmbient = false
            });
            Channels.Add(new TempChannelConfig
            {
                ChannelIndex = 5,
                TagName = "Temp.CH5",
                PointName = "U相绕组PT100",
                IsEnabled = false,
                PointType = "内部绕组",
                IsAmbient = false
            });
            Channels.Add(new TempChannelConfig
            {
                ChannelIndex = 6,
                TagName = "Temp.CH6",
                PointName = "V相绕组PT100",
                IsEnabled = false,
                PointType = "内部绕组",
                IsAmbient = false
            });
            Channels.Add(new TempChannelConfig
            {
                ChannelIndex = 7,
                TagName = "Temp.CH7",
                PointName = "W相绕组PT100",
                IsEnabled = false,
                PointType = "内部绕组",
                IsAmbient = false
            });
            Channels.Add(new TempChannelConfig
            {
                ChannelIndex = 8,
                TagName = "Temp.CH8",
                PointName = "备用测点",
                IsEnabled = false,
                PointType = "常规测点",
                IsAmbient = false
            });
        }

        public void SaveConfig()
        {
            try
            {
                string? dir = Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // 支持中文字符不被转义为 \uXXXX
                };

                string json = JsonSerializer.Serialize(Channels, options);
                File.WriteAllText(_configFilePath, json);
                _logger.Information("测温通道配置已成功持久化至 temp_channels.json");

                ChannelsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "保存测温通道配置文件失败！");
            }
        }
    }
}
