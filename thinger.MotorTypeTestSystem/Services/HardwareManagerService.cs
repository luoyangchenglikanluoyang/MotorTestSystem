using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using thinger.MotorTypeTestSystem.ViewModels;

namespace thinger.MotorTypeTestSystem.Services
{
    public class HardwareManagerService : IHardwareManagerService
    {
        private readonly IDeviceManagerService _deviceManager;
        private readonly ILogger _logger;

        public ObservableCollection<DeviceRuntimeItem> DeviceList { get; }

        public HardwareManagerService(IDeviceManagerService deviceManager, ILogger logger)
        {
            _deviceManager = deviceManager;
            _logger = logger;

            DeviceList = new ObservableCollection<DeviceRuntimeItem>();

            // 在服务初始化时，就把设备列表加载好
            foreach (var config in _deviceManager.DeviceConfigs)
            {
                DeviceList.Add(new DeviceRuntimeItem(config, _logger));
            }
        }

        public async Task AutoConnectAllAsync()
        {
            var tasks = new List<Task>();
            foreach (var device in DeviceList)
            {
                // 只有勾选了“开机自连”的设备，并且当前未连接，才会去尝试连接！
                if (!device.IsConnected && device.Config.AutoConnect)
                {
                    tasks.Add(device.ConnectCommand.ExecuteAsync(null));
                }
            }
            await Task.WhenAll(tasks);
        }

        public async Task DisconnectAllAsync()
        {
            var tasks = new List<Task>();
            foreach (var device in DeviceList)
            {
                if (device.IsConnected)
                {
                    tasks.Add(device.DisconnectCommand.ExecuteAsync(null));
                }
            }
            await Task.WhenAll(tasks);
        }

        public void SaveConfig()
        {
            _deviceManager.SaveConfigs();
        }
    }
}
