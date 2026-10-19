using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using thinger.MotorTypeTestSystem.Models.Hardware;

namespace thinger.MotorTypeTestSystem.Services
{
    /// <summary>
    /// 设备管理器接口
    /// </summary>
    public interface IDeviceManagerService
    {
        /// <summary>
        /// 内存中的设备配置列表
        /// </summary>
        List<DeviceConfig> DeviceConfigs { get; }
        /// <summary>
        /// 初始化：从 JSON 读取配置，如果没有 JSON 则生成一个默认的
        /// </summary>
        void InitializeConfigs();
        /// <summary>
        /// 将当前内存中的配置保存回 JSON 文件
        /// </summary>
        void SaveConfigs();
    }
}
