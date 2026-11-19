using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace thinger.MotorTypeTestSystem.Models.NoLoadTestModels
{
    /// <summary>
    /// GB/T 1032-2023 单个空载测试点数据模型
    /// </summary>
    public partial class NoLoadPointModel : ObservableObject
    {
        /// <summary>
        /// 点序号（第 1 点 ~ 第 8 点）
        /// </summary>
        public int PointIndex { get; set; }

        #region 三相电参数测量值 (实测全线值)
        /// <summary>
        /// 三相平均线电压 U₀ (V)
        /// </summary>
        [ObservableProperty]
        private double _voltage;

        // 分相测量线电压 (V)
        [ObservableProperty] private double _uA; // 对应 Uab
        [ObservableProperty] private double _uB; // 对应 Ubc
        [ObservableProperty] private double _uC; // 对应 Uca

        /// <summary>
        /// 三相平均线电流 I₀ (A)（三相算术平均值）
        /// </summary>
        [ObservableProperty]
        private double _current;

        // 分相测量线电流 (A)
        [ObservableProperty] private double _iA; // 对应 Ia
        [ObservableProperty] private double _iB; // 对应 Ib
        [ObservableProperty] private double _iC; // 对应 Ic

        /// <summary>
        /// 三相总输入有功功率 P₀ (W)
        /// </summary>
        [ObservableProperty]
        private double _inputPower;

        /// <summary>
        /// 功率因数 cosφ₀
        /// </summary>
        [ObservableProperty]
        private double _powerFactor;

        /// <summary>
        /// 电网频率 f (Hz)
        /// </summary>
        [ObservableProperty]
        private double _frequency = 50.0;

        /// <summary>
        /// 三相电流不平衡度 (%)
        /// </summary>
        [ObservableProperty]
        private double _currentUnbalance;
        #endregion

        #region 转速与温度 (GB/T 1032 强规范)
        /// <summary>
        /// 实测转速 n (r/min)
        /// </summary>
        [ObservableProperty]
        private double _speed;

        /// <summary>
        /// 转差率 s = (ns - n) / ns
        /// </summary>
        [ObservableProperty]
        private double _slip;

        /// <summary>
        /// 该点测量时的定子绕组温度 θ₀ (℃)（GB/T 1032 第 9.2 条规定）
        /// </summary>
        [ObservableProperty]
        private double _windingTemp = 25.0;

        /// <summary>
        /// 该点温度换算得到的定子端电阻 R₀ (Ω)
        /// 公式：R0 = R1 * (K1 + θ0) / (K1 + θ1)
        /// </summary>
        [ObservableProperty]
        private double _statorResistance;
        #endregion

        #region 国标自动计算损耗与拟合参数
        /// <summary>
        /// 定子铜耗 Pcu0 (W) = 1.5 * I₀² * R₀（R0 为线端电阻）
        /// </summary>
        [ObservableProperty]
        private double _statorCopperLoss;

        /// <summary>
        /// 恒定损耗（扣除铜耗后净损耗）Pc 或 P₀' (W) = P₀ - Pcu0
        /// </summary>
        [ObservableProperty]
        private double _netLoss;

        /// <summary>
        /// 该电压点下的铁芯损耗 Pfe (W) = Pc - Pfw（用于绘制国标图 6 的 Pfe 曲线）
        /// </summary>
        [ObservableProperty]
        private double _ironLoss;

        /// <summary>
        /// 实测电压标幺值 U₀ / UN（标幺值即百分比）
        /// </summary>
        [ObservableProperty]
        private double _voltageRatio;

        /// <summary>
        /// 实测电压标幺值平方 (U₀ / UN)²（机械耗拟合横坐标）（标幺值即百分比）
        /// </summary>
        public double VoltageRatioSquared => VoltageRatio * VoltageRatio;
        #endregion

        /// <summary>
        /// 采集时间
        /// </summary>
        public DateTime SampleTime { get; set; } = DateTime.Now;
    }
}
