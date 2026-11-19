using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace thinger.MotorTypeTestSystem.Models.TestHelpers
{
    /// <summary>
    /// GB/T 1032 冷却曲线最小二乘法线性回归数学引擎(用于温升试验倒推停机电阻)
    /// </summary>
    public class CoolingCurveHelper
    {
        /// <summary>
        /// 拟合直线：R(t) = Slope * t + R0
        /// </summary>
        /// <param name="points">至少 2 个点的数据集合</param>
        /// <returns>(R0: t=0秒理论停机阻值, Slope: 冷却降温斜率, R2: 拟合优度判定系数)</returns>
        public static (double R0, double Slope, double R2) CalculateExtrapolation(List<CoolingPoint> points)
        {
            if (points == null || points.Count < 2)
                return (0, 0, 0);
            int n = points.Count;
            double sumX = 0;
            double sumY = 0;
            double sumXX = 0;
            double sumXY = 0;
            double sumYY = 0;
            foreach (var pt in points)
            {
                sumX += pt.Seconds;
                sumY += pt.Resistance;
                sumXX += pt.Seconds * pt.Seconds;
                sumXY += pt.Seconds * pt.Resistance;
                sumYY += pt.Resistance * pt.Resistance;
            }
            double meanX = sumX / n;
            double meanY = sumY / n;
            // 最小二乘法求斜率 Slope 和截距 R0
            double denominator = sumXX - n * meanX * meanX;
            if (Math.Abs(denominator) < 1e-12)
                return (0, 0, 0);
            double slope = (sumXY - n * meanX * meanY) / denominator;
            double r0 = meanY - slope * meanX;
            // 计算拟合优度 R² (越接近 1.0 拟合质量越完美)
            double totalVariation = sumYY - n * meanY * meanY;
            double r2 = 1.0;
            if (totalVariation > 1e-12)
            {
                double explainedVariation = slope * (sumXY - n * meanX * meanY);
                r2 = Math.Max(0, Math.Min(1.0, explainedVariation / totalVariation));
            }
            return (Math.Round(r0, 5), Math.Round(slope, 6), Math.Round(r2, 4));
        }
    }
    /// <summary>
    /// 冷却外推单次打点数据实体
    /// </summary>
    public class CoolingPoint
    {
        public int Index { get; set; }           // 序号 (1, 2, 3...)
        public double Seconds { get; set; }       // 停机后流逝秒数 (如 21.4s)
        public double Resistance { get; set; }    // 该时刻实测阻值 (如 0.65210 Ω)
    }
}
