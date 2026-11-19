using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;

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

        #region 三相电测量参数 (功率分析仪/电网)
        /// <summary>
        /// Uab 线电压 (V)
        /// </summary>
        [ObservableProperty]
        private double _voltageUab;

        /// <summary>
        /// Ubc 线电压 (V)
        /// </summary>
        [ObservableProperty]
        private double _voltageUbc;

        /// <summary>
        /// Uca 线电压 (V)
        /// </summary>
        [ObservableProperty]
        private double _voltageUca;

        /// <summary>
        /// 三相平均线电压 U (V)
        /// </summary>
        [ObservableProperty]
        private double _voltage;

        /// <summary>
        /// Ia 线电流 (A)
        /// </summary>
        [ObservableProperty]
        private double _currentIa;

        /// <summary>
        /// Ib 线电流 (A)
        /// </summary>
        [ObservableProperty]
        private double _currentIb;

        /// <summary>
        /// Ic 线电流 (A)
        /// </summary>
        [ObservableProperty]
        private double _currentIc;

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

        #region 各部位实测温度 (核心统计汇总与动态各通道快照)
        /// <summary>
        /// 冷却介质 / 室内环境基准温度 (℃)
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

        /// <summary>
        /// 本次工况记录时，所有已启用通道的真实测点名称与实测温度字典快照
        /// Key: 通道及测点名称 (如 "通道 2(U相端部)")
        /// Value: 实测温度值 (℃)
        /// </summary>
        public Dictionary<string, double> ChannelTemps { get; set; } = new();

        /// <summary>
        /// 本次工况记录时，按物理通道编号索引的温度字典 (Key: 1, 2, 3...)
        /// </summary>
        public Dictionary<int, double> ChannelTempsByIndex { get; set; } = new();

        /// <summary>
        /// 通道索引器：供前台 DataGrid 动态温度列直接使用 [ChannelIndex] 绑定
        /// </summary>
        public double? this[int channelIndex]
        {
            get
            {
                // 1. 如果已记录了本通道的具体温度（包含 0.0℃），直接返回实测值
                if (ChannelTempsByIndex != null && ChannelTempsByIndex.TryGetValue(channelIndex, out var val))
                    return val;

                // 2. 兼容历史数据兜底（针对本次升级前存入数据库的历史工况记录，未保存 ChannelTempsByIndex）
                if (channelIndex == 1) return AmbientTemp; // 通道1通常是环境基准
                if (channelIndex == 2) return MotorTemp;   // 通道2通常是机壳/绕组
                if (channelIndex == 3) return BearingTemp; // 通道3通常是前轴承
                if (channelIndex == 4) return BearingTemp; // 通道4通常是后轴承

                return null;
            }
            set
            {
                if (value.HasValue)
                    ChannelTempsByIndex[channelIndex] = value.Value;
                else
                    ChannelTempsByIndex.Remove(channelIndex);
            }
        }
        #endregion
    }
}
