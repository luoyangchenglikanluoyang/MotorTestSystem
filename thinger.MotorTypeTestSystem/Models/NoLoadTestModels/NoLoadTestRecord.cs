using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace thinger.MotorTypeTestSystem.Models.NoLoadTestModels
{
    /// <summary>
    /// 空载试验结果与归档记录实体类（对应数据库 motor_no_load 表）
    /// </summary>
    public partial class NoLoadTestRecord : ObservableObject
    {
        public int Id { get; set; }

        /// <summary>
        /// 【核心外键】关联主表流水号 MotorTestRecord.Id
        /// </summary>
        public string RecordId { get; set; } = string.Empty;

        /// <summary>
        /// 试验归档时间
        /// </summary>
        public DateTime TestTime { get; set; } = DateTime.Now;

        #region 冷态与环境基准快照
        /// <summary>
        /// 基准冷态定子端电阻 R1 (Ω)
        /// </summary>
        [ObservableProperty] private double _coldResistance;

        /// <summary>
        /// 测量冷态电阻时的温度 θ1 (℃)
        /// </summary>
        [ObservableProperty] private double _coldResistanceTemp = 25.0;

        /// <summary>
        /// 试验时环境温度 (℃)
        /// </summary>
        [ObservableProperty] private double _ambientTemp = 25.0;
        #endregion

        #region 额定工况（100% Un）测量与计算参数
        /// <summary>
        /// 额定空载电压 U0N (V)
        /// </summary>
        [ObservableProperty] private double _ratedNoLoadVoltage;

        /// <summary>
        /// 额定空载电流 I₀ (A)
        /// </summary>
        [ObservableProperty] private double _ratedNoLoadCurrent;

        /// <summary>
        /// 空载电流比 I₀ / IN (%)
        /// </summary>
        [ObservableProperty] private double _currentRatio;

        /// <summary>
        /// 额定空载总输入损耗 P₀ (W)
        /// </summary>
        [ObservableProperty] private double _ratedNoLoadPower;

        /// <summary>
        /// 额定空载功率因数 cosφ₀
        /// </summary>
        [ObservableProperty] private double _ratedPowerFactor;

        /// <summary>
        /// 额定空载转速 n₀ (r/min)
        /// </summary>
        [ObservableProperty] private double _ratedNoLoadSpeed;

        /// <summary>
        /// 额定空载定子铜耗 Pcu0 (W)
        /// </summary>
        [ObservableProperty] private double _ratedStatorCopperLoss;

        /// <summary>
        /// 额定恒定损耗 Pc (W) = P₀ - Pcu0 = Pfe + Pfw
        /// </summary>
        [ObservableProperty] private double _ratedConstantLoss;
        #endregion

        #region 曲线拟合与损耗分离结果（GB/T 1032 核心）
        /// <summary>
        /// 曲线外推至 U=0 的截距损耗 Pfw0 (W)（未修正机械损耗）
        /// </summary>
        [ObservableProperty] private double _uncorrectedMechanicalLoss;

        /// <summary>
        /// 转差率修正后的机械风摩损耗 Pfw (W) = Pfw0 * (1 - s)^2.5
        /// </summary>
        [ObservableProperty] private double _mechanicalLoss;

        /// <summary>
        /// 额定铁芯损耗 Pfe (W) = Pc(100% Un) - Pfw
        /// </summary>
        [ObservableProperty] private double _ironLoss;

        /// <summary>
        /// 风摩耗线性回归拟合优度 R² (通常应 ≥ 0.98)
        /// </summary>
        [ObservableProperty] private double _regressionR2;
        #endregion

        #region 判定与标准限值
        /// <summary>
        /// 允许的最大空载电流比限值 (%)
        /// </summary>
        [ObservableProperty] private double _maxAllowedCurrentRatio = 50.0;

        /// <summary>
        /// 允许的最大额定空载损耗限值 (W)
        /// </summary>
        [ObservableProperty] private double _maxAllowedNoLoadPower;

        /// <summary>
        /// 综合判定是否合格
        /// </summary>
        [ObservableProperty] private bool? _isQualified;
        #endregion

        #region 试验报表与追溯
        /// <summary>
        /// 8 个点位原始测量数据序列化 JSON
        /// </summary>
        public string PointsJson { get; set; } = string.Empty;

        /// <summary>
        /// 空载特性曲线与损耗分离曲线截图文件相对路径
        /// </summary>
        public string CurveImagePath { get; set; } = string.Empty;
        #endregion
    }
}