using HandyControl.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace thinger.MotorTypeTestSystem.Models
{
    /// <summary>
    /// 型式试验电机信息表（主表）
    /// 对应一台被测电机在某次试验中的建档与基本铭牌参数
    /// </summary>
    public class MotorTestRecord
    {
        #region 主键标识与生成

        /// <summary>
        /// 试验记录唯一流水号 (主键)
        /// 规则：出厂条码 + 试验时间戳 (例如：SN-20260902-001_20260904103001)
        /// </summary>
        public string Id { get; set; } = string.Empty;//string.Empty 本质上就是 ""（空字符串，也就是一对中间什么都没有的双引号）。
                                                      //它代表一个长度为 0 的文本对象。它和 null 有什么区别？
                                                      //很多初学者容易把 null 和 string.Empty（即 ""）搞混，用现实中的东西打比方：
                                                      //null：空气/不存在。你手里连盒子都没有。如果你试图打开盒子拿东西（比如执行 Id.Length 或 Id.Trim()），程序会直接崩溃闪退（报经典的 NullReferenceException 空指针异常）。
                                                      //string.Empty：一个空盒子。盒子的确存在于内存中，只是里面装的内容长度是 0。你执行 Id.Length 它会正常返回 0，执行 Id.Trim() 也不会报错。

        #endregion

        #region 电机参数与试验配置 (与 ViewModel 顺序严格一致)

        /// <summary>
        /// 被测电机型号
        /// </summary>
        public string MotorModel { get; set; } = string.Empty;

        /// <summary>
        /// 测试条码 / 出厂编号
        /// </summary>
        public string MotorSerialNumber { get; set; } = string.Empty;

        /// <summary>
        /// 试验时间
        /// </summary>
        public DateTime TestTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 生产时间
        /// </summary>
        public DateTime ProductionTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 额定功率 (kW)
        /// </summary>
        public double RatedPower { get; set; }

        /// <summary>
        /// 额定电压 (V)
        /// </summary>
        public double RatedVoltage { get; set; }

        /// <summary>
        /// 额定电流 (A)
        /// </summary>
        public double RatedCurrent { get; set; }

        /// <summary>
        /// 额定频率 (Hz)
        /// </summary>
        public double RatedFrequency { get; set; }

        /// <summary>
        /// 额定转矩 (N·m)
        /// </summary>
        public double RatedTorque { get; set; }

        /// <summary>
        /// 额定转速 (rpm)
        /// </summary>
        public double RatedSpeed { get; set; }

        /// <summary>
        /// 标称效率 (%)
        /// </summary>
        public double NominalEfficiency { get; set; }

        /// <summary>
        /// 标称功率因数 (cosφ)
        /// </summary>
        public double RatedPowerFactor { get; set; }

        /// <summary>
        /// 转动惯量 (kg·m²)
        /// </summary>
        public double RotationalInertia { get; set; }

        /// <summary>
        /// 极数 / 级数 (P)
        /// </summary>
        public int Series { get; set; }

        /// <summary>
        /// 工作制 (如 S1、S2)
        /// </summary>
        public string WorkSystem { get; set; } = "S1 (连续工作制)";

        /// <summary>
        /// 连接方式 (星接 / 三角接)
        /// </summary>
        public string ConnectionMethod { get; set; } = "星接";

        /// <summary>
        /// 绝缘等级 (A / E / B / F / H / C)
        /// </summary>
        public string InsulationClass { get; set; } = "B";

        /// <summary>
        /// 防护等级 (IP55、IP65 等)
        /// </summary>
        public string ProtectionLevel { get; set; } = "IP55";

        /// <summary>
        /// 温升等级
        /// </summary>
        public string? TemperatureRiseClass { get; set; }

        /// <summary>
        /// 定子绕组材料常数 K1 (铜: 235, 铝: 225)
        /// </summary>
        public int K1 { get; set; } = 235;

        /// <summary>
        /// 转子绕组材料常数 K2 (铜: 235, 铝: 225)
        /// </summary>
        public int K2 { get; set; } = 235;

        /// <summary>
        /// 试验方法 / 遵循标准
        /// </summary>
        public string SelectedStandard { get; set; } = "GB/T 1032-2012 三相异步电动机试验方法";

        #endregion

        #region 试验流转补充字段 (用于测试归档与状态管理)

        /// <summary>
        /// 当前试验状态 (0: 进行中, 1: 合格, 2: 不合格)
        /// </summary>
        public int TestResultStatus { get; set; } = 0;

        /// <summary>
        /// 归档完成时间 (未完成时可为空)
        /// </summary>
        public DateTime? CompleteTime { get; set; }

        #endregion

        #region 便捷生成主键方法

        /// <summary>
        /// 生成统一规则的主键 ID
        /// </summary>
        public static string GenerateId(string serialNumber, DateTime time)
        {
            return $"{serialNumber.Trim()}_{time:yyyyMMddHHmmss}";
        }

        #endregion
    }
}
