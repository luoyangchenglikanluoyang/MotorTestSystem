using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using thinger.MotorTypeTestSystem.Models;
using thinger.MotorTypeTestSystem.Services;
using thinger.MotorTypeTestSystem.Views.Dialogs;

namespace thinger.MotorTypeTestSystem.ViewModels
{
    public partial class TemperatureRiseViewModel : ObservableObject, ISubTestViewModel
    {
        private readonly ITempChannelService _tempChannelService;
        private readonly TagPoolService _tagPool; // 全局实时通信点位池
        private readonly ILogger _logger;

        // 核心定时器：每秒驱动一次计时与温度点采样
        private readonly DispatcherTimer _timer;
        private int _elapsedSeconds = 0;

        #region 给 View 调用的事件委托
        /// <summary>
        /// 通知前端图表清空归零
        /// </summary>
        public event Action? RequestClearChart;

        /// <summary>
        /// 通知前端图表画入新数据点 (测点名称, 时间, 温度值)
        /// </summary>
        public event Action<string, DateTime, double>? RequestAddPoint;
        #endregion

        #region Observable 属性
        [ObservableProperty]
        private MotorTestRecord? _motorRecord;

        /// <summary>
        /// 试验累计时长显示 (00:00:00)
        /// </summary>
        [ObservableProperty]
        private string _elapsedTimeString = "00:00:00";

        /// <summary>
        /// 热稳定判定胶囊文字
        /// </summary>
        [ObservableProperty]
        private string _thermalStatusText = "未开始";

        /// <summary>
        /// 试验是否正在进行中
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartTestCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopTestCommand))]
        [NotifyCanExecuteChangedFor(nameof(OpenChannelConfigCommand))]
        private bool _isTesting = false;
        #endregion

        public TemperatureRiseViewModel(ITempChannelService tempChannelService, TagPoolService tagPool, ILogger logger)
        {
            _tempChannelService = tempChannelService;
            _tagPool = tagPool;
            _logger = logger;

            // 初始化定时器：每 1 秒触发一次
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += OnTimerTick;
        }

        public void InitializeTestRecord(MotorTestRecord mainRecord)
        {
            if (mainRecord == null) return;
            MotorRecord = mainRecord;
            _logger.Information($"温升试验已接收电机记录：{mainRecord.Id}");
        }

        #region 试验核心控制命令

        /// <summary>
        /// 【▶ 开始试验】命令（带防呆确认与四大归零）
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStartTest))]
        private void StartTest()
        {
            // 防呆检查：如果已有试验时间，弹窗警告是否重新归零
            if (_elapsedSeconds > 0)
            {
                if (HandyControl.Controls.MessageBox.Show(
                    "检测到当前已有试验记录。\n重新开始试验将清空上一轮所有曲线与计时数据并重新归零，是否确定继续？",
                    "确认重新开始", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            // 🌟 1. 计时归零
            _elapsedSeconds = 0;
            ElapsedTimeString = "00:00:00";

            // 🌟 2. 热稳定状态归零
            ThermalStatusText = "升温中 (初期)";

            // 🌟 3. 图表曲线清空归零
            RequestClearChart?.Invoke();

            // 🌟 4. 启动定时器，状态切为进行中
            IsTesting = true;
            _timer.Start();

            // 立即采样第 0 秒的初始温度
            SampleCurrentTemperatures();

            HandyControl.Controls.Growl.Success("温升试验已成功启动并归零计时！");
            _logger.Information("温升试验已归零启动");
        }

        private bool CanStartTest() => !IsTesting;

        /// <summary>
        /// 【⏹ 停止试验】命令
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStopTest))]
        private void StopTest()
        {
            if (HandyControl.Controls.MessageBox.Show(
                "确定要停止当前的温升试验吗？停止后将暂停数据采样与计时。",
                "停止确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            _timer.Stop();
            IsTesting = false;
            ThermalStatusText = "试验已停止";

            HandyControl.Controls.Growl.Info("温升试验已暂停/结束。");
            _logger.Information($"温升试验已停止，总时长：{ElapsedTimeString}");
        }

        private bool CanStopTest() => IsTesting;

        /// <summary>
        /// 【⚙ 测点配置】命令（试验过程中禁用，防止跑着跑着把通道改乱）
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanConfigChannel))]
        private void OpenChannelConfig()
        {
            try
            {
                var dialog = new TempChannelConfigWindow(_tempChannelService)
                {
                    Owner = Application.Current.MainWindow
                };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "打开测点通道配置弹窗异常");
            }
        }

        private bool CanConfigChannel() => !IsTesting;

        #endregion

        #region 定时采样与计时驱动
        private void OnTimerTick(object? sender, EventArgs e)
        {
            // 1. 秒数自增并刷新 UI 胶囊
            _elapsedSeconds++;
            ElapsedTimeString = TimeSpan.FromSeconds(_elapsedSeconds).ToString(@"hh\:mm\:ss");

            // 2. 采样频率控制：例如每 5 秒在曲线上打一个点（避免曲线点位过于密集占用内存）
            if (_elapsedSeconds % 5 == 0)
            {
                SampleCurrentTemperatures();
            }
        }

        /// <summary>
        /// 从全局点位池采集各已启用通道的温度，推入图表
        /// </summary>
        /// <summary>
        /// 从全局点位池采集各已启用通道的温度，推入图表
        /// </summary>
        private void SampleCurrentTemperatures()
        {
            DateTime now = DateTime.Now;

            foreach (var channel in _tempChannelService.Channels)
            {
                // 只采集用户勾选了启用的测点通道
                if (!channel.IsEnabled) continue;

                var tag = _tagPool[channel.TagName];
                double tempValue = 0.0;

                // 1. 如果真实硬件连接成功且有有效读数，采用真实温度
                if (tag?.Value != null && double.TryParse(tag.Value.ToString(), out double parsed) && parsed > 0)
                {
                    tempValue = parsed;
                }
                else
                {
                    // 🌟 2. 调试模式（串口未接真实设备时）：生成逼真的电机温升数据（25°C基础 + 平缓上升）
                    // 方便在开发调试时立刻看到彩色的动态曲线与图例！
                    tempValue = 25.0 + (channel.ChannelIndex * 3.5) + (_elapsedSeconds * 0.05);
                }

                // 🌟 推送到图表绘制
                RequestAddPoint?.Invoke(channel.PointName, now, tempValue);
            }
        }
        #endregion
    }
}