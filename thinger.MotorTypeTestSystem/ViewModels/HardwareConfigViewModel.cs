using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using Serilog;
using System.Collections.ObjectModel;
using thinger.Mexico50Towdolly.ModbusRTU;
using thinger.MotorTypeTestSystem.Models.Hardware;
using thinger.MotorTypeTestSystem.Services;

namespace thinger.MotorTypeTestSystem.ViewModels
{
    // ==========================================
    // 1. 运行时设备包装类 (负责状态与UI交互)
    // ==========================================
    public partial class DeviceRuntimeItem : ObservableObject
    {
        public DeviceConfig Config { get; }

        // 绿灯状态：是否已连接
        [ObservableProperty]
        private bool _isConnected;

        // 加载状态：是否正在连接中（防止疯狂连点）
        [ObservableProperty]
        private bool _isConnecting;

        // 智能显示地址（自动处理 IP 或 串口）
        public string DisplayAddress
        {
            get => Config.CommType == CommunicationType.ModbusTCP ? Config.IpAddress : Config.ComPort;
            set
            {
                if (IsConnected || IsConnecting) return; // 连上时禁止修改底层数据

                if (Config.CommType == CommunicationType.ModbusTCP)
                    Config.IpAddress = value;
                else
                    Config.ComPort = value;
                OnPropertyChanged();
            }
        }

        // 智能显示端口（自动处理 端口 或 波特率）
        public string DisplayPort
        {
            get => Config.CommType == CommunicationType.ModbusTCP ? Config.Port.ToString() : Config.BaudRate.ToString();
            set
            {
                if (IsConnected || IsConnecting) return; // 连上时禁止修改底层数据

                if (Config.CommType == CommunicationType.ModbusTCP)
                {
                    if (int.TryParse(value, out int p)) Config.Port = p;
                }
                else
                {
                    if (int.TryParse(value, out int b)) Config.BaudRate = b;
                }
                OnPropertyChanged();
            }
        }

        private ILogger _Logger;

        public DeviceRuntimeItem(DeviceConfig config, ILogger logger)
        {
            Config = config;
            _Logger = logger;
        }

        // 异步连接命令
        [RelayCommand]
        private async Task ConnectAsync()
        {
            if (IsConnected || IsConnecting) return;
            IsConnecting = true;

            try
            {
                object service = ConnectionManager.GetService(Config);
                BindLogger(service);

                if (Config.CommType == CommunicationType.ModbusTCP)
                {
                    var tcp = (ModbusTCPService)service;//这是将service强转为ModbusTCPService类型
                    await tcp.ConnectAsync();
                }
                else
                {
                    var rtu = (ModbusRTUService)service;//这是将service强转为ModbusRTUService类型
                    // 打开对应的串口
                    await rtu.ConnectAsync(Config.ComPort, Config.BaudRate, 8, System.IO.Ports.Parity.None, System.IO.Ports.StopBits.One);
                }

                IsConnected = true;
                Growl.Success($"{Config.DeviceName} 连接成功！");
            }
            catch (Exception ex)
            {
                Growl.Error($"{Config.DeviceName} 连接失败: {ex.Message}");
            }
            finally
            {
                IsConnecting = false;
            }
        }

        // 断开命令
        [RelayCommand]
        private async Task DisconnectAsync()
        {
            if (!IsConnected) return;

            try
            {
                IsConnected = false; // 先逻辑断开（变灰灯）

                // ConnectionManager管家自动处理减设备数，没设备了就会物理关串口
                await ConnectionManager.ReleaseServiceAsync(Config);

                Growl.Info($"{Config.DeviceName} 已断开。");
            }
            catch (Exception ex)
            {
                Growl.Error($"断开时出错: {ex.Message}");
            }
        }

        // 【新增】：将底层的日志文本，智能路由到 Serilog 的不同级别中
        private void BindLogger(object commService)
        {
            Action<string> logAction = (msg) =>
            {
                // 根据关键字智能分类日志级别
                if (msg.Contains("错误") || msg.Contains("失败") || msg.Contains("致命") || msg.Contains("异常"))
                {
                    _Logger.Error(msg);
                }
                else if (msg.Contains("警告") || msg.Contains("超时"))
                {
                    _Logger.Warning(msg);
                }
                else
                {
                    _Logger.Information(msg);
                }
            };
            // 把这个方法绑到底层服务的喇叭上
            if (commService is ModbusTCPService tcp)
            {
                tcp.LogAction = logAction;
            }
            else if (commService is ModbusRTUService rtu)
            {
                rtu.LogAction = logAction;
            }
        }
    }
    public partial class HardwareConfigViewModel : ObservableObject
    {
        public string Title { get; } = "通信诊断与配置";

        // 直接把整个全局服务暴露给前端绑定
        public IHardwareManagerService HardwareManager { get; }

        // 构造函数注入全局服务
        public HardwareConfigViewModel(IHardwareManagerService hardwareManager)
        {
            HardwareManager = hardwareManager;
        }

        // 界面上的手动一键连接按钮（调用底层服务）
        [RelayCommand]
        private async Task ConnectAllAsync()
        {
            await HardwareManager.AutoConnectAllAsync();
            Growl.Success("全部连接指令执行完毕！");
        }

        // 界面上的手动一键断开按钮
        [RelayCommand]
        private async Task DisconnectAllAsync()
        {
            await HardwareManager.DisconnectAllAsync();
            Growl.Success("全部断开指令执行完毕！");
        }

        [RelayCommand]
        private void SaveConfig()
        {
            HardwareManager.SaveConfig();
            Growl.Success("硬件参数已安全更新至配置文件！");
        }
    }
}

