using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace thinger.MotorTypeTestSystem.Models
{
    /// <summary>
    /// 电机温升试验数据实体类（对应数据库表 motor_temperature_rise）
    /// </summary>  
    public partial class TemperatureRiseRecord : ObservableObject
    {           
        public int Id { get; set; }

        /// <summary>
        /// 【核心外键】关联主表 MotorTestRecord.Id 流水号
        /// </summary>
        public string RecordId { get; set; } = string.Empty;

        #region 冷态基准参数
        /// <summary>
        /// 冷态绕组平均电阻 R1 (Ω)
        /// </summary>
        [ObservableProperty]
        private double _coldResistance;

        /// <summary>
        /// 冷态测量时环境/绕组温度 θ1 (℃)
        /// </summary>
        [ObservableProperty]
        private double _coldTemp = 25.0;

        #endregion

        #region 停机热态测量参数
        /// <summary>
        /// 热态 U-V 线电阻 (Ω)
        /// </summary>
        [ObservableProperty]
        private double _hotRuv;

        /// <summary>
        /// 热态 V-W 线电阻 (Ω)
        /// </summary>
        [ObservableProperty]
        private double _hotRvw;

        /// <summary>
        /// 热态 W-U 线电阻 (Ω)
        /// </summary>
        [ObservableProperty]
        private double _hotRwu;

        /// <summary>
        /// 热态三相平均电阻 R2 (Ω)
        /// </summary>
        [ObservableProperty]
        private double _hotResistance;

        /// <summary>
        /// 停机时最终环境温度 θ2 (℃)
        /// </summary>
        [ObservableProperty]
        private double _shutdownAmbientTemp = 25.0;

        /// <summary>
        /// 轴承最高实测温度 (℃)
        /// </summary>
        [ObservableProperty]
        private double _maxBearingTemp;

        /// <summary>
        /// 允许轴承最高温度限值 (℃)（滚动轴承默认 95℃，滑动轴承可改为 80℃）
        /// </summary>
        [ObservableProperty]
        private double _allowedBearingTemp = 95.0;
        #endregion
        #region 国标自动计算结果
        /// <summary>
        /// 电阻法推算出的绕组内部热态温度 t2 (℃)
        /// 公式：t2 = (R2 / R1) * (K + θ1) - K
        /// </summary>
        [ObservableProperty]
        private double _hotWindingTemp;

        /// <summary>
        /// 实际考核温升 Δθ = (R2-R1)/R1 × （K+θ1）+(θ1-θ2)
        /// </summary>
        [ObservableProperty]
        private double _temperatureRise;

        /// <summary>
        /// 允许温升限值 (K)（根据绝缘等级，如 F级一般为 100K 或 105K）
        /// </summary>
        [ObservableProperty]
        private double _allowedTempRise = 100.0;

        /// <summary>
        /// 最终合格判定结果
        /// </summary>
        [ObservableProperty]
        private bool? _isQualified = null;

        #endregion
        #region 试验追溯信息
        /// <summary>
        /// 试验累计运行时长 (例如 "02:15:30")
        /// </summary>
        [ObservableProperty]
        private string _testDuration = "00:00:00";

        /// <summary>
        /// 曲线原始数据 JSON 文件相对路径
        /// </summary>
        public string CurveDataFilePath { get; set; } = string.Empty;

        /// <summary>
        /// 曲线高清截图 PNG 文件相对路径
        /// </summary>
        public string CurveImagePath { get; set; } = string.Empty;

        /// <summary>
        /// 试验归档完成时间
        /// </summary>
        public DateTime TestTime { get; set; } = DateTime.Now;

        /// <summary>
        /// GB/T 1032-2023 过程工况快照记录集合序列化 JSON 文本 (对应数据库 SnapshotsJson 字段)
        /// </summary>
        public string SnapshotsJson { get; set; } = string.Empty;
        #endregion

    }
}
