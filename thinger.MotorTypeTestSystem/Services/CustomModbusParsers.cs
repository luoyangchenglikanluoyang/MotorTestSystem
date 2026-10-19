using System;
using System.Collections.Generic;

namespace thinger.MotorTypeTestSystem.Services
{
    /// <summary>
    /// 【解药库】专门用于存放奇葩设备的自定义解析逻辑
    /// 使用静态类的好处：不需要注册依赖注入，随调随用，不会让采集引擎变得臃肿。
    /// </summary>
    public static class CustomModbusParsers
    {
        // 这是一个字典，Key 是你在 tags.json 里配置的名字，Value 是具体的解析方法。
        // Func<ushort[], int, object> 的意思是：传入原始数组和偏移量，吐出一个结果（可能是 float 也可能是 int 等）
        public static readonly Dictionary<string, Func<ushort[], int, object>> Parsers = new()
        {
            {
                "算法A_温度除以10", (rawData, offset) =>
                {
                    // 有些 PLC 不支持浮点，发过来是 1234，实际代表 123.4 度
                    ushort rawValue = rawData[offset];
                    return (float)rawValue / 10f;
                }
            },
            {
                "算法B_拼接提取中间位", (rawData, offset) =>
                {
                    // 假设某个厂家，要求拿第一个寄存器的高 8 位，和第二个寄存器的低 8 位拼起来
                    ushort word1 = rawData[offset];
                    ushort word2 = rawData[offset + 1];

                    byte highByte = (byte)(word1 >> 8);
                    byte lowByte = (byte)(word2 & 0xFF);

                    // 拼成一个特殊的值
                    int specialValue = (highByte << 8) | lowByte;

                    return (float)specialValue;
                }
            },
            {
                "电桥_毫欧转欧姆", (rawData, offset) =>
                {
                    // rawData 就是从电桥读上来的 ushort[] 寄存器数组
                    // 假设两个寄存器拼成一个 32位 整数 (毫欧)
                    int milliOhm = (rawData[offset] << 16) | rawData[offset + 1];
                    // 毫欧除以 1000 变成欧姆(Ω)返回
                    return (double)milliOhm / 1000.0;
             }
    }
        };
    }
}
