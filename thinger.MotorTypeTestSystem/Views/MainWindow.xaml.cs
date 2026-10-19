using Prism.Navigation.Regions;
using System.Security.Policy;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using thinger.MotorTypeTestSystem.ViewModels;

namespace thinger.MotorTypeTestSystem.Views
{
    public partial class MainWindow : Window
    {
        private readonly IRegionManager _regionManager;

        public MainWindow(MainWindowViewModel viewModel, IRegionManager regionManager)
        {
            InitializeComponent();
            DataContext = viewModel;
            _regionManager = regionManager;

            //Loaded += (s, e) =>
            //{
            //    // 默认初始加载场景一：任务大厅
            //    _regionManager.RequestNavigate("MainRegion", "TaskCenterView");
            //};
        }

        // 点击标签页右上角的 × 号触发的事件
        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;//as button是尝试将sender（触发事件的对象（即用户点击的那个“关闭”按钮））。
                                          //试将它转换为 Button 类型,as 用来“尝试”把一种类型转换成另一种类型。
                                          //转换成功就返回新类型的对象，转换失败不会报错，而是返回 null**。
            if (button == null) return;

            // 沿着视觉树向上寻找当前的 TabItem（即当前TabControl的子控件，存放的是页面例如CreateTestViewModel）
            DependencyObject current = button;//从按钮本身开始向上遍历。
            while (current != null && !(current is TabItem))
            {
                current = VisualTreeHelper.GetParent(current);//寻找当前控件的父控件然后赋值给current
            }

            if (current is TabItem tabItem)
            {
                // TabItem 的 Content 就是被 Prism 注入进来的具体视图 (如 TaskCenterView)
                var viewToRemove = tabItem.Content;
                if (viewToRemove != null)
                {
                    // 获取视图对应的 ViewModel
                    var viewModel = (viewToRemove as FrameworkElement)?.DataContext;

                    // 1. 检查 ViewModel 是否实现了 IConfirmNavigationRequest
                    if (viewModel is IConfirmNavigationRequest confirmRequestItem)
                    {
                        // 调用拦截确认 (传递 null 作为 NavigationContext，因为这里是手动 Remove 而不是 Navigate)
                        //NavigationContext 本身的作用：当你在 Prism 中使用 _regionManager.RequestNavigate("MainRegion", "ViewB?id=123") 时，Prism 会自动生成一个包含目标地址、URL 参数、原页面信息的上下文对象，传给被切走的页面。
                        //这里为什么传 null：当前场景不是页面之间的路由导航跳转，而是用户手动点击了右上角的 × 按钮，强制执行视图移除。此时根本没有发生 Prism 路由，自然也就没有任何 NavigationContext 对象。
                        confirmRequestItem.ConfirmNavigationRequest(null, (canClose) =>
                        {
                            if (canClose)
                            {
                                RemoveViewAndDestroy(viewToRemove);
                            }
                        });
                    }
                    else
                    {
                        // 没有实现拦截接口，直接关闭并清理
                        RemoveViewAndDestroy(viewToRemove);
                    }
                }
            }
        }

        private void RemoveViewAndDestroy(object viewToRemove)
        {
            // 1. 从 Region 中移除
            _regionManager.Regions["MainRegion"].Remove(viewToRemove);

            // 2. 尝试获取 ViewModel
            var viewModel = (viewToRemove as FrameworkElement)?.DataContext;

            // 3. 手动触发 Destroy 释放资源
            if (viewModel is Prism.Navigation.IDestructible destructibleVM)
            {
                destructibleVM.Destroy();
            }

            if (viewToRemove is Prism.Navigation.IDestructible destructibleView)
            {
                destructibleView.Destroy();
            }
        }

        // ================== 自定义标题栏按钮事件 ==================
        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
                BtnMaximize.Content = "\xE922"; // 变回最大化图标
            }
            else
            {
                this.WindowState = WindowState.Maximized;
                BtnMaximize.Content = "\xE923"; // 变成还原图标
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
