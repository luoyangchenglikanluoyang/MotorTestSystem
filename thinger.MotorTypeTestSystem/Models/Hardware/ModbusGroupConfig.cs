using System.Collections.Generic;

namespace thinger.MotorTypeTestSystem.Models.Hardware
{
    /// <summary>
    /// 代表一个需要“一次性打包读取”的数据块（比如一口气读 20 个寄存器）
    /// </summary>
    public class ModbusGroupConfig
    {
        // 这个块属于哪个设备？（对应 DeviceConfig 里的 DeviceId）
        public string DeviceId { get; set; } =string.Empty;

        // 这个组的名字，起个名字方便记，比如 "主控PLC_核心数据"
        public string GroupName { get; set; } = string.Empty;

        // 功能码：1(读线圈), 2(读离散输入), 3(读保持寄存器), 4(读输入寄存器)
        public byte FunctionCode { get; set; } = 3;

        // 起始地址，比如 0 代表 40001
        public ushort StartAddress { get; set; }

        // 一口气读多少个寄存器（比如 10）
        public ushort Length { get; set; }

        // 读回来的大数组，需要被切分成哪些具体的变量？
        public List<ModbusTagConfig> Tags { get; set; } = new List<ModbusTagConfig>();
    }

    /// <summary>
    /// 代表大数组里面的一个具体的变量切片
    /// </summary>
    public class ModbusTagConfig
    {
        // 映射到变量池里的名字，比如 "IO.Voltage_U"
        public string TagName { get; set; } = string.Empty;

        // 偏移量（在这个大数组里的第几个位置？比如 0）
        public int Offset { get; set; }

        // 👇 加这个属性！代表如果它是隐藏在寄存器里的 Bool 量，我要提取第几位？
        // 默认给个 0，免得以前的配置报错。
        public int BitIndex { get; set; } = 0;

        // 大小端序类型
        public string ByteOrder { get; set; } = string.Empty;

        // 数据类型（比如 "Float", "UShort", "Bool", "Custom"）
        public string DataType { get; set; } = string.Empty;

        // 【新增】如果是 Custom 自定义类型，去哪个字典里找专属解药？
        public string CustomMethod { get; set; } = string.Empty;
    }
}
