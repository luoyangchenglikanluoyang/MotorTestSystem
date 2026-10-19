using System;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Prism.Navigation.Regions;

namespace thinger.MotorTypeTestSystem.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly IRegionManager _regionManager;

        [ObservableProperty]
        private string _currentTimeString = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        [ObservableProperty]
        private string _currentOperator = "测试工程师";

        [ObservableProperty]
        private string _selectedNavMenu = "TaskCenterView";

        public MainWindowViewModel(IRegionManager regionManager)
        {
            _regionManager = regionManager;

            // 启动时钟更新（此操作不会有新的线程被创建，这个DispatcherTimer 是依附于主 UI 线程运行的。
            // 它就像是主线程日程表里的一个“闹钟”。
            // 当你的 WPF 应用程序关闭时，主 UI 线程就被操作系统杀死了。如果不是在主窗口，而是在一个临时弹出的子窗口（比如刚才的“历史查询页面”），里面写了一个 DispatcherTimer。那你必须要手动关闭它！）
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += (s, e) =>
            {
                CurrentTimeString = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            };
            timer.Start();

        }

        [RelayCommand]
        public void Navigate(string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName)) return;//如果传入的视图名称是 null、空字符串或全是空格，直接退出方法，不执行任何操作。
            SelectedNavMenu = viewName;
            _regionManager.RequestNavigate("MainRegion", viewName);
        }
    }
}
