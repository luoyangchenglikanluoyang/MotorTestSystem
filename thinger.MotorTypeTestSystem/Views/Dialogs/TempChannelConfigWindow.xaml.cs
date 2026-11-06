using HandyControl.Controls;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
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

        /// <summary>
        /// 环境基准复选框点击事件：实现“单选互斥”防呆
        /// 勾选任意一个通道为环境基准，自动取消其他所有通道的勾选
        /// </summary>
        private void ChkAmbient_Click(object sender, RoutedEventArgs e)
        {
            // 1. 拿到被点击的 CheckBox 和当前这一行的数据实体
            if (sender is CheckBox chk && chk.DataContext is TempChannelConfig current)
            {
                // 2. 如果用户是把它打上了勾
                if (chk.IsChecked == true)
                {
                    // 3. 遍历所有通道，把除了它以外的其他通道全部设为 false
                    foreach (var item in Channels)
                    {
                        if (item != current)
                        {
                            item.IsAmbient = false;
                            // 贴心联动：如果其他通道原本下拉选了“环境基准”，顺便帮它恢复成常规测点
                            if (item.PointType == "环境基准")
                            {
                                item.PointType = "常规测点";
                            }
                        }       
                        current.PointType = "环境基准";
                    }
                }
                else
                {
                    // 如果用户试图把唯一打勾的给取消掉，可以自动保持勾选（强制必须选一个）
                    // 或者留给用户取消，保存按钮处的防呆机制会提示他
                }
                // 4. 当前通道的测点类型自动同步为“环境基准”
            }
        }
    }
}
