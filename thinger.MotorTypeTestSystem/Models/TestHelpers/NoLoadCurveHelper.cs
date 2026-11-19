using System;
using System.Collections.Generic;
using System.Linq;
using thinger.MotorTypeTestSystem.Models.NoLoadTestModels;

namespace thinger.MotorTypeTestSystem.Models.TestHelpers
{
    /// <summary>
    /// GB/T 1032-2023 空载试验数学计算与损耗分离引擎
    /// </summary>
    public static class NoLoadCurveHelper
    {
        /// <summary>
        /// 执行 GB/T 1032 空载试验损耗分离与额定特性计算
        /// </summary>
        /// <param name="points">8 个采集点列表</param>
        /// <param name="ratedVoltage">额定电压 UN (V)</param>
        /// <param name="ratedCurrent">额定电流 IN (A)</param>
        /// <param name="record">输出回填的试验记录实体</param>
        /// <returns>计算是否成功</returns>
        public static (bool Success, string Message) CalculateLossSeparation(List<NoLoadPointModel> points,double ratedVoltage,double ratedCurrent,NoLoadTestRecord record)
        {
            if (points == null || record == null)
                return (false, "试验数据为空！");

            // 1. 筛出有效点，并按实测电压比 U0/UN 从小到大排序（后面插值必须用有序列表）
            var validPoints = points
                .Where(p => p.Voltage > 0 && p.VoltageRatio > 0)
                .OrderBy(p => p.VoltageRatio)
                .ToList();
            if (validPoints.Count < 7)
                return (false, $"当前有效点仅 {validPoints.Count} 个，国标要求不少于 7 个点，请继续采点！");

            // 2. 提取低压段数据点（实测电压比 U0/UN <= 0.60，用于拟合机械风摩损耗）
            var lowVoltagePoints = validPoints
                .Where(p => p.VoltageRatio <= 0.60 && p.NetLoss > 0)
                .OrderBy(p => p.VoltageRatioSquared)
                .ToList();
            if (lowVoltagePoints.Count < 3)
                return (false, $"低压段(U₀/UN ≤ 0.60)有效点仅 {lowVoltagePoints.Count} 个，不少于 3 个点才能准确外推机械损耗！");
            
            // 2. 最小二乘法线性回归：
            // 自变量 X = (U0 / UN)²（实测电压标幺值平方）
            // 因变量 Y = Pc = P0 - Pcu0（扣除定子铜耗后的净损耗/恒定损耗）
            // 拟合方程：Y = Slope * X + Pfw0
            var (slope, pfw0, r2) = CalculateRegression(lowVoltagePoints);

            record.UncorrectedMechanicalLoss = Math.Round(Math.Max(0, pfw0), 2);
            record.RegressionR2 = Math.Round(r2, 4);

            // 检查是否覆盖了额定电压附近的高压区（至少要有实测电压 >= 0.95 UN 的高压点）
            if (validPoints.Max(p => p.VoltageRatio) < 0.95)
                return (false, "缺少额定电压(1.0 UN)附近的高压测试点，请至少采点至 1.0~1.1 UN！");

            // 3. 按国标 9.3.1：用线性插值从曲线上求取精确 U0/UN = 1.0 时的额定空载参数
            record.RatedNoLoadVoltage = Math.Round(ratedVoltage, 2);
            record.RatedNoLoadCurrent = Math.Round(InterpolateAtRated(validPoints, p => p.Current), 3);
            record.RatedNoLoadPower = Math.Round(InterpolateAtRated(validPoints, p => p.InputPower), 2);
            record.RatedPowerFactor = Math.Round(InterpolateAtRated(validPoints, p => p.PowerFactor), 4);
            record.RatedNoLoadSpeed = Math.Round(InterpolateAtRated(validPoints, p => p.Speed), 1);
            record.RatedStatorCopperLoss = Math.Round(InterpolateAtRated(validPoints, p => p.StatorCopperLoss), 2);
            record.RatedConstantLoss = Math.Round(InterpolateAtRated(validPoints, p => p.NetLoss), 2);
            // 计算额定空载电流比 I0 / IN (%)
            if (ratedCurrent > 0)
            {
                record.CurrentRatio = Math.Round((record.RatedNoLoadCurrent / ratedCurrent) * 100.0, 2);
            }
            // 4. 机械风摩损耗转差率修正：Pfw = Pfw0 * (1 - s)^2.5
            double ratedSlip = InterpolateAtRated(validPoints, p => p.Slip);
            ratedSlip = Math.Max(0, Math.Min(0.1, ratedSlip));
            record.MechanicalLoss = Math.Round(record.UncorrectedMechanicalLoss * Math.Pow(1.0 - ratedSlip, 2.5), 2);
            // 5. 额定铁芯损耗：Pfe = Pc(100% Un) - Pfw
            record.IronLoss = Math.Round(Math.Max(0, record.RatedConstantLoss - record.MechanicalLoss), 2);
            // 6. 给每个采集点填上铁芯损耗 Pfe = Pc - Pfw（供国标图 6 画 Pfe 曲线）
            foreach (var pt in validPoints)
            {
                pt.IronLoss = Math.Round(Math.Max(0, pt.NetLoss - record.MechanicalLoss), 2);
            }
            // 7. 综合合格判定
            bool currentPass = record.CurrentRatio <= record.MaxAllowedCurrentRatio;
            bool powerPass = record.MaxAllowedNoLoadPower <= 0 || record.RatedNoLoadPower <= record.MaxAllowedNoLoadPower;
            record.IsQualified = currentPass && powerPass;

            return (true,"空载试验核算已完成");
        }

        /// <summary>
        /// 低压段最小二乘法线性回归计算器
        /// </summary>
        /// <param name="lowVoltagePoints">低压测试点</param>
        /// <returns>(Slope: 斜率, Pfw0: 截距机械风摩损耗, R2: 拟合优度)</returns>
        /// Pfw0 ---直线截距 𝑏----未修正机械风摩损耗----空载试验的终极目标之一。用额定总损耗减去它，就能算出发热的额定铁耗Pfe
        /// Slope----直线斜率 𝑘----损耗随电压平方上升的速度----给 ScottPlot 图表画回归线和外推虚线提供斜率参数。
        /// R2	-----判定系数 𝑅²----直线拟合的完美程度 (0∼1)	----判定系数 𝑅质量防呆：界面展示给用户看，若 R²>=0.98证明本次试验极高精度、完全合格。
        public static (double Slope, double Pfw0, double R2) CalculateRegression(List<NoLoadPointModel> lowVoltagePoints)
        {
            if (lowVoltagePoints == null || lowVoltagePoints.Count < 2)
                return (0, 0, 0);

            int n = lowVoltagePoints.Count;
            double sumX = 0, sumY = 0, sumXX = 0, sumXY = 0, sumYY = 0;

            foreach (var pt in lowVoltagePoints)
            {
                double x = pt.VoltageRatioSquared;
                double y = pt.NetLoss;
                sumX += x;
                sumY += y;
                sumXX += x * x;
                sumXY += x * y;
                sumYY += y * y;
            }

            double meanX = sumX / n;
            double meanY = sumY / n;
            double denominator = sumXX - n * meanX * meanX;

            if (Math.Abs(denominator) < 1e-12)
                return (0, 0, 0);

            double slope = (sumXY - n * meanX * meanY) / denominator;
            double pfw0 = meanY - slope * meanX;

            // 拟合优度判定系数 R²
            double totalVariation = sumYY - n * meanY * meanY;
            double r2 = 1.0;
            if (totalVariation > 1e-12)
            {
                double explainedVariation = slope * (sumXY - n * meanX * meanY);
                r2 = Math.Max(0, Math.Min(1.0, explainedVariation / totalVariation));
            }

            return (slope, pfw0, r2);
        }

        /// <summary>
        /// 在按电压比排好序的点集中，线性插值求取 VoltageRatio = 1.0 处的精确值
        /// </summary>
        private static double InterpolateAtRated(List<NoLoadPointModel> sortedPoints,Func<NoLoadPointModel, double> selector)
        {
            // 1. 刚好有几乎等于 1.0 的点，直接返回
            var exact = sortedPoints.FirstOrDefault(p => Math.Abs(p.VoltageRatio - 1.0) < 0.001);
            if (exact != null) return selector(exact);

            // 2. 找 1.0 左边最近的点，和 1.0 右边最近的点
            var left = sortedPoints.LastOrDefault(p => p.VoltageRatio < 1.0);
            var right = sortedPoints.FirstOrDefault(p => p.VoltageRatio > 1.0);

            // 3. 如果左右都有点，做两点线性插值
            if (left != null && right != null)
            {
                double t = (1.0 - left.VoltageRatio) / (right.VoltageRatio - left.VoltageRatio);
                return selector(left) + t * (selector(right) - selector(left));
            }

            // 4. 兜底：如果全在 1.0 同一侧，取最接近 1.0 的那个点
            var nearest = sortedPoints.OrderBy(p => Math.Abs(p.VoltageRatio - 1.0)).First();
            return selector(nearest);
        }
    }
}
