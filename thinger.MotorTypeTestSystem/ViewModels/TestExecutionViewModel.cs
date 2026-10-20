using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using thinger.MotorTypeTestSystem.Models;
using thinger.MotorTypeTestSystem.Services;
using thinger.MotorTypeTestSystem.Views;

namespace thinger.MotorTypeTestSystem.ViewModels
{
    // 注意：必须加上 partial 关键字，否则 [ObservableProperty] 无法自动生成属性
    public partial class TestExecutionViewModel : ObservableObject, IConfirmNavigationRequest, IDestructible, IRegionMemberLifetime
    {
        private readonly ILogger _logger;

        // 用于控制试验过程中后台任务取消的令牌源
        //private CancellationTokenSource _cts;

        /// <summary>
        /// IContainerProvider是prism的取出接口，用来取出已经注册过的页面
        /// </summary>
        private readonly IContainerProvider _containerProvider;

        // 核心缓存字典：存下当前这台电机已经点开过的子试验界面，来回切换绝不丢数据！
        private readonly Dictionary<string, FrameworkElement> _subViewCache = new();

        // 动态绑定到 Tab 标签页头部的标题
        [ObservableProperty]
        private string _title = "试验中心";

        // 接收从“新建测试”传过来的电机建档信息
        [ObservableProperty]
        private MotorTestRecord? _currentRecord;

        // 绑定到前端 ContentControl 的当前子试验界面
        [ObservableProperty]
        private FrameworkElement? _currentSubView;

        public TestExecutionViewModel(ILogger logger, IContainerProvider containerProvider)
        {
            _logger = logger;
            _containerProvider = containerProvider;
        }

        public bool KeepAlive => true;

        #region 点击左侧悬浮导航栏时触发：切换子试验界面
        [RelayCommand]
        private void SwitchSubView(string viewKey)
        {
            //检查传递进来的参数viewKey是否为空，如果是则直接返回
            if (string.IsNullOrWhiteSpace(viewKey))
            {
                return;
            }

            // 1.!_subViewCache.TryGetValue(viewKey, out FrameworkElement targetView)的作用是
            // 先看缓存字典里有没有建过这个子界面（如果建过，直接拿出来赋值给targetView，保留之前采的数据）
            // if这一行代码等与先定义一个局部变量FrameworkElement targetView;
            // 然后 bool found = _subViewCache.TryGetValue(viewKey，out targetView);
            // 然后再将found判断是false还是true。
            // TryGetValue：尝试获取。成功返回 true，失败返回 false。
            if (!_subViewCache.TryGetValue(viewKey, out FrameworkElement? targetView))
            {
                // 2. 如果字典里没有，说明是第一次点开这个试验项，从容器里新建它
                targetView = viewKey switch
                {
                    // 这里根据后续新建的 UserControl 名字一一对应即可
                    "Nameplate" => _containerProvider.Resolve<NamePlateView>(),
                    "DcResistance" => _containerProvider.Resolve<DcResistanceView>(),
                    // "NoLoad"       => _containerProvider.Resolve<NoLoadTestView>(),
                    _ => null
                };

                if (targetView != null)
                {
                    // 3. 【关键一步】如果这个子界面的 ViewModel 实现了 ISubTestViewModel，
                    // 就把主表的 CurrentRecord（含流水号 Id、额定电压功率等）当场传给它！
                    if (targetView.DataContext is ISubTestViewModel subVm && CurrentRecord != null)
                    {
                        subVm.InitializeTestRecord(CurrentRecord);
                    }

                    // 4. 放进缓存字典，下次切回来直接用
                    _subViewCache[viewKey] = targetView;
                }
            }
            // 5. 把拿到的页面
            // 赋值给 CurrentSubView。
            // 前端的 ContentControl 看到 CurrentSubView 变了，瞬间把页面显示在主屏幕上！
            CurrentSubView = targetView;
        }
        #endregion

        #region 导航与关闭拦截权（防误关）
        /// <summary>
        /// 拦截导航/关闭，决定是否放行
        /// </summary>
        /// <param name="navigationContext"></param>
        /// <param name="continuationCallback"></param>
        /// <exception cref="NotImplementedException"></exception>
        public void ConfirmNavigationRequest(NavigationContext navigationContext, Action<bool> continuationCallback)
        {
            if (navigationContext!=null)
            {
                continuationCallback(true);
                return;
            }

            var result = HandyControl.Controls.MessageBox.Show(
                    "确定要关闭此页面吗？",
                    "提示",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (result == MessageBoxResult.No)
            {
                // 传入 false，直接阻止关闭/导航动作
                continuationCallback(false);
                return;
            }
            else
            {
                continuationCallback(true);
            }
        }
        /// <summary>
        /// 临终清理与彻底释放资源（防内存泄漏）
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public void Destroy()
        {
            // 当整个“试验中心”标签页被关闭时，顺手把所有缓存的子试验也清理掉
            foreach (var view in _subViewCache.Values)
            {
                // 关键防呆：必须确保不是自己 (view.DataContext != this)，防止递归死循环！
                if (view.DataContext != null && view.DataContext != this && view.DataContext is IDestructible destructibleSubVm)
                {
                    destructibleSubVm.Destroy();
                }
            }
            _subViewCache.Clear();
        }

        /// <summary>
        /// 要不要复用当前实例
        /// </summary>
        /// <param name="navigationContext"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            // 如果传过来的电机流水号和当前页面的一模一样，直接跳到这个已打开的 Tab，不新建
            if (navigationContext.Parameters.ContainsKey("CurrentTestRecord"))
            {
                var targetRecord = navigationContext.Parameters.GetValue<MotorTestRecord>("CurrentTestRecord");
                return CurrentRecord != null && CurrentRecord.Id == targetRecord?.Id;
            }

            // 如果是不同的电机，返回 false，Prism 会自动为你再开一个新的 Tab 页
            return false;
        }

        /// <summary>
        /// 离开页面时清理
        /// </summary>
        /// <param name="navigationContext"></param>
        /// <exception cref="NotImplementedException"></exception>
        public void OnNavigatedFrom(NavigationContext navigationContext)
        {

        }
        /// <summary>
        /// 进入页面时初始化
        /// </summary>
        /// <param name="navigationContext">这个变量包含了导航传递过来的信息以及参数</param>
        /// <exception cref="NotImplementedException"></exception>
        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            // 检查导航参数里有没有传过来 CurrentTestRecord
            if (navigationContext.Parameters.ContainsKey("CurrentTestRecord"))
            {
                CurrentRecord = navigationContext.Parameters.GetValue<MotorTestRecord>("CurrentTestRecord");

                if (CurrentRecord != null)
                {
                    // 动态修改标签页标题，方便操作员区分是哪台电机
                    Title = $"试验执行 - {CurrentRecord.MotorSerialNumber}";
                    _logger.Information($"进入试验执行界面，当前流水号：{CurrentRecord.Id}");

                    // 如果已经有缓存的子页面，同步更新它们的电机档案
                    foreach (var view in _subViewCache.Values)
                    {
                        if (view.DataContext is ISubTestViewModel subVm)
                        {
                            subVm.InitializeTestRecord(CurrentRecord);
                        }
                    }

                    // 默认一进来自动切换到第一个页面：“铭牌信息”
                    SwitchSubView("Nameplate");
                }
            }
        }
        #endregion
    }
}
