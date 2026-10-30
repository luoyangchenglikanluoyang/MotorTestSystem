using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace thinger.MotorTypeTestSystem.Models
{
    /// <summary>
    /// GB/T 1032-2023 温升试验过程工况记录明细行
    /// (S1 连续工作制每隔 30 分钟记录一次或操作员手动采集)
    /// </summary>
    public partial class TempRiseProcessItem : ObservableObject
    {
        /// <summary>
        /// 序号 (1, 2, 3...)，支持动态刷新
        /// </summary>
        [ObservableProperty]
        private int _index;

        /// <summary>
        /// 试验累计运行时长 (如 "00:00:00", "00:30:00")
        /// </summary>
        [ObservableProperty]
        private string _elapsedTimeString = "00:00:00";

        /// <summary>
        /// 采集时间戳 (如 "14:30:25")
        /// </summary>
        [ObservableProperty]
        private DateTime _timestamp = DateTime.Now;

        /// <summary>
        /// 格式化时间字符串
        /// </summary>
        public string TimeString => Timestamp.ToString("HH:mm:ss");

        #region 电气测量参数 (功率分析仪/电网)
        /// <summary>
        /// 三相平均线电压 U (V)
        /// </summary>
        [ObservableProperty]
        private double _voltage;

        /// <summary>
        /// 三相平均线电流 I (A)
        /// </summary>
        [ObservableProperty]
        private double _current;

        /// <summary>
        /// 输入有功功率 P1 (kW)
        /// </summary>
        [ObservableProperty]
        private double _activePower;

        /// <summary>
        /// 功率因数 cosφ
        /// </summary>
        [ObservableProperty]
        private double _powerFactor;

        /// <summary>
        /// 供电频率 f (Hz)
        /// </summary>
        [ObservableProperty]
        private double _frequency = 50.0;
        #endregion

        #region 机械参数 (测功机/扭矩转速仪)
        /// <summary>
        /// 电机转速 n (r/min)
        /// </summary>
        [ObservableProperty]
        private double _speed;

        /// <summary>
        /// 输出转矩 Td (N·m)
        /// </summary>
        [ObservableProperty]
        private double _torque;
        #endregion

        #region 各部位实测温度 (多路巡检仪)
        /// <summary>
        /// 冷却介质 / 室内环境温度 (℃)
        /// </summary>
        [ObservableProperty]
        private double _ambientTemp;

        /// <summary>
        /// 绕组内部或机壳表面最高温度 (℃)
        /// </summary>
        [ObservableProperty]
        private double _motorTemp;

        /// <summary>
        /// 轴承最高温度 (℃)
        /// </summary>
        [ObservableProperty]
        private double _bearingTemp;
        #endregion
    }
}
