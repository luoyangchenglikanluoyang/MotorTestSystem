using HandyControl.Controls;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using thinger.MotorTypeTestSystem.Models;
using thinger.MotorTypeTestSystem.Services;

namespace thinger.MotorTypeTestSystem.Views.Dialogs
{
    /// <summary>
    /// 测温通道与测点设置弹窗交互逻辑
    /// </summary>
    public partial class TempChannelConfigWindow : System.Windows.Window
    {
        private readonly ITempChannelService _tempChannelService;

        public ObservableCollection<TempChannelConfig> Channels => _tempChannelService.Channels;

        public TempChannelConfigWindow(ITempChannelService tempChannelService)
        {
            InitializeComponent();
            _tempChannelService = tempChannelService;
            DataContext = this;
        }

        /// <summary>
        /// 允许鼠标按住顶部拖拽移动弹窗
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        /// <summary>
        /// 点击取消或右上角关闭按钮
        /// </summary>
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// 保存并应用配置
        /// </summary>
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // 防呆检查：检查是否有勾选环境基准
            bool hasAmbient = false;
            foreach (var ch in Channels)
            {
                if (ch.IsEnabled && (ch.IsAmbient || ch.PointType == "环境基准"))
                {
                    hasAmbient = true;
                    ch.IsAmbient = true; // 确保一致
                    break;
                }
            }

            if (!hasAmbient)
            {
                HandyControl.Controls.MessageBox.Show(
                    "请至少指定一个启用的测点为【环境基准】（用来计算温升），否则无法准确判定温升限值！",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 持久化存储到本地 JSON 文件并触发更新事件
            _tempChannelService.SaveConfig();
            Growl.Success("测点配置已更新并应用！");

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// 恢复出厂默认通道配置
        /// </summary>
        private void BtnResetDefault_Click(object sender, RoutedEventArgs e)
        {
            var result = HandyControl.Controls.MessageBox.Show(
                "确定要恢复默认的 8 路测温通道配置吗？当前修改将会被覆盖。",
                "确认恢复",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _tempChannelService.LoadConfig(); // 重新加载或走默认
                Growl.Info("已重新加载测温通道设置");
            }
        }
    }
}
