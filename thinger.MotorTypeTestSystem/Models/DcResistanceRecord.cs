using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace thinger.MotorTypeTestSystem.Models
{
    /// <summary>
    /// 直流电阻试验数据实体类（对应子表 motor_dc_resistance）
    /// </summary>
    public partial class DcResistanceRecord: ObservableObject
    {
        /// <summary>
        /// 子表自增主键
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 【核心外键】关联主表 MotorTestRecord.Id 的流水号
        /// </summary>
        public string RecordId { get; set; } = string.Empty;

        /// <summary>
        /// U-V 线电阻 (Ω)
        /// </summary>
        [ObservableProperty]
        private double _ruv;

        /// <summary>
        /// V-W 线电阻 (Ω)
        /// </summary>
        [ObservableProperty]
        private double _rvw;

        /// <summary>
        /// W-U 线电阻 (Ω)
        /// </summary>
        [ObservableProperty]
        private double _rwu;

        /// <summary>
        /// 测量时绕组温度 / 环境温度 (℃)
        /// </summary>
        [ObservableProperty]
        private double _windingTemp = 25.0;

        /// <summary>
        /// 三相平均线电阻 (Ω)
        /// </summary>
        [ObservableProperty]
        private double _averageResistance;

        /// <summary>
        /// 三相电阻不平衡度 (%)
        /// </summary>
        [ObservableProperty]
        private double _unbalanceRate;

        /// <summary>
        /// 折算到 20℃ 时的标准电阻 (Ω)
        /// </summary>
        [ObservableProperty]
        private double _resistanceAt20C;

        // 允许的不平衡度上限阈值（默认给国标的 2.0%）
        [ObservableProperty]
        private double _maxAllowedUnbalance = 2.0;

        /// <summary>
        /// 判定结果是否合格 (比如不平衡度 ≤ 2% 为合格)
        /// </summary>
        [ObservableProperty]
        private bool _isQualified = true;

        /// <summary>
        /// 保存时间
        /// </summary>
        [ObservableProperty]
        private DateTime _testTime = DateTime.Now;
    }
}
