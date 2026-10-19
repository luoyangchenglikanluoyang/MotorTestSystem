using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace thinger.MotorTypeTestSystem.Models.Hardware
{
    /// <summary>
    /// 设备通讯类型枚举
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))] //此为attribute特性 将枚举保存为字符串，方便人阅读（让枚举（Enum）在转成 JSON 数据时，以“字符串名称”呈现，而不是默认的“数字”。）
    public enum CommunicationType
    {
        ModbusTCP,
        ModbusRTU
    }

    /// <summary>
    /// 设备的配置模型（直接与 JSON 文件映射）
    /// </summary>
    public class DeviceConfig
    {
        /// <summary>
        /// 设备的唯一标识（如：PowerAnalyzer、PLC_1）
        /// </summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>
        /// 设备的显示名称（如：功率分析仪、主控PLC）
        /// </summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>
        /// 通信类型 (TCP 或 RTU)
        /// </summary>
       
        public CommunicationType CommType { get; set; } = CommunicationType.ModbusTCP;

        /// <summary>
        /// 是否在软件启动时自动连接
        /// </summary>
        public bool AutoConnect { get; set; } = true;

        // ================= Modbus TCP 参数 =================
        public string IpAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 502;
        // ================= Modbus RTU 参数 =================
        public string ComPort { get; set; } = "COM1";
        public int BaudRate { get; set; } = 9600;

        /// <summary>
        /// 从站地址 (RTU和TCP有时候都需要)
        /// </summary>
        public byte SlaveId { get; set; } = 1;
    }
}
