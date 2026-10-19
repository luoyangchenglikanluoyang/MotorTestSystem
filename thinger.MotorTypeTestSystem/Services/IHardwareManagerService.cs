using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using thinger.MotorTypeTestSystem.ViewModels;

namespace thinger.MotorTypeTestSystem.Services
{
    public interface IHardwareManagerService
    {
        // 维护全局唯一的一份设备运行状态列表
        ObservableCollection<DeviceRuntimeItem> DeviceList { get; }

        // 全局所有设备连接
        Task AutoConnectAllAsync();

        // 全局所有设备断开
        Task DisconnectAllAsync();

        // 保存配置
        void SaveConfig();
    }
}
