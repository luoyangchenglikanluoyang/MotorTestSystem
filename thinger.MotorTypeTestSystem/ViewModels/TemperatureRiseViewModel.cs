using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Windows;
using thinger.MotorTypeTestSystem.Models;
using thinger.MotorTypeTestSystem.Services;
using thinger.MotorTypeTestSystem.Views.Dialogs;

namespace thinger.MotorTypeTestSystem.ViewModels
{
    /// <summary>
    /// 温升试验 ViewModel
    /// </summary>
    public partial class TemperatureRiseViewModel : ObservableObject, ISubTestViewModel
    {
        private readonly ITempChannelService _tempChannelService;
        private readonly ILogger _logger;

        /// <summary>
        /// 当前关联的电机主记录
        /// </summary>
        [ObservableProperty]
        private MotorTestRecord? _motorRecord;

        public TemperatureRiseViewModel(ITempChannelService tempChannelService, ILogger logger)
        {
            _tempChannelService = tempChannelService;
            _logger = logger;
        }

        /// <summary>
        /// 当切换到本页面时，总控台自动把电机的建档信息传进来
        /// </summary>
        public void InitializeTestRecord(MotorTestRecord mainRecord)
        {
            if (mainRecord == null) return;
            MotorRecord = mainRecord;
            _logger.Information($"温升试验已接收电机记录：{mainRecord.Id}");
        }

        /// <summary>
        /// 点击【⚙ 测点通道设置】按钮，弹出模态通道设置弹窗
        /// </summary>
        [RelayCommand]
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
            catch (System.Exception ex)
            {
                _logger.Error(ex, "打开测点通道配置弹窗异常");
            }
        }
    }
}
