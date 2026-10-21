using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using thinger.MotorTypeTestSystem.Models;
using thinger.MotorTypeTestSystem.Services;

namespace thinger.MotorTypeTestSystem.ViewModels
{
    /// <summary>
    /// 历史查询ViewModel
    /// </summary>
    public partial class HistoryQueryViewModel : ObservableObject, INavigationAware
    {
        private readonly IMotorTestRepository _repo;
        private readonly IRegionManager _regionManager;
        private readonly ILogger _logger;

        // 动态绑定到 Tab 标签页头部的标题
        [ObservableProperty]
        private string _title = "历史试验";

        // 搜索关键词（支持输入条码或型号）
        [ObservableProperty]
        private string _searchKeyword = string.Empty;
        // 绑定给 DataGrid 表格的数据源列表
        [ObservableProperty]
        private ObservableCollection<MotorTestRecord> _records = new();
        // 当前在表格里选中的那一行记录
        [ObservableProperty]
        private MotorTestRecord? _selectedRecord;
        // 加载状态标记
        [ObservableProperty]
        private bool _isLoading;
        // 总记录数统计
        [ObservableProperty]
        private int _totalCount;
        public HistoryQueryViewModel(IMotorTestRepository repo, IRegionManager regionManager, ILogger logger)
        {
            _repo = repo;
            _regionManager = regionManager;
            _logger = logger;
        }

        /// <summary>
        /// 命令一：执行搜索与查询
        /// </summary>
        [RelayCommand]
        private async Task Query()
        {
            try
            {
                IsLoading = true;
                var list = await _repo.QueryTestRecordsAsync(SearchKeyword);

                Records.Clear();
                foreach (var item in list)
                {
                    Records.Add(item);
                }
                TotalCount = Records.Count;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "查询历史测试记录失败");
                Growl.Error($"查询历史记录失败：{ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 命令二：重置搜索框并刷新全部列表
        /// </summary>
        [RelayCommand]
        private void Reset()
        {
            SearchKeyword = string.Empty;
            _ = Query();
        }

        /// <summary>
        /// 命令三：【核心】进入该电机的试验现场！
        /// 把当前选中的电机数据打包，瞬间导航跳转到 TestExecutionView
        /// </summary>
        [RelayCommand]
        private void OpenExecution(MotorTestRecord? record)
        {
            var target = record ?? SelectedRecord;
            if (target == null)
            {
                Growl.Warning("请先选择一条要进入的试验记录！");
                return;
            }
            try
            {
                // 将选中的这台电机档案打包进导航参数
                var parameters = new NavigationParameters
                {
                    { "CurrentTestRecord", target }
                };
                // 呼叫 Prism 导航引擎，瞬间切进试验执行台！
                _regionManager.RequestNavigate("MainRegion", "TestExecutionView", parameters);
                Growl.Success($"已载入电机 [{target.MotorSerialNumber}] 的试验现场！");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "进入试验中心失败");
                Growl.Error($"进入试验中心失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 命令四：删除记录（带防手滑确认弹窗）
        /// </summary>
        [RelayCommand]
        private async Task DeleteRecord(MotorTestRecord? record)
        {
            var target = record ?? SelectedRecord;
            if (target == null) return;
            var result = HandyControl.Controls.MessageBox.Show(
                $"确定要删除电机 [{target.MotorSerialNumber}] 的所有试验数据吗？\n流水号：{target.Id}\n（此操作不可恢复）",
                "警告",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            try
            {
                bool success = await _repo.DeleteTestRecordAsync(target.Id);
                if (success)
                {
                    Records.Remove(target);
                    TotalCount = Records.Count;
                    Growl.Success($"已成功删除电机 [{target.MotorSerialNumber}] 的试验记录！");
                }
                else
                {
                    Growl.Warning("删除失败，未在数据库中找到该记录！");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"删除测试记录 [{target.Id}] 失败");
                Growl.Error($"删除记录失败：{ex.Message}");
            }
        }

        #region Prism 导航接口实现
        /// <summary>
        /// 当用户从主界面菜单点击“📁 历史查询”切进来时触发
        /// </summary>
        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            // 一进页面自动查询最新的记录，不用每次都去点一下“查询”按钮！
            _ = Query();
        }
        public bool IsNavigationTarget(NavigationContext navigationContext) => true;
        public void OnNavigatedFrom(NavigationContext navigationContext) { }

       
        #endregion


    }
}
