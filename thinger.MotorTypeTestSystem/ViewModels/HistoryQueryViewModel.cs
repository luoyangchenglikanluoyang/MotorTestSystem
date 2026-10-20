using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using thinger.MotorTypeTestSystem.Models;
using thinger.MotorTypeTestSystem.Services;

namespace thinger.MotorTypeTestSystem.ViewModels
{
    /// <summary>
    /// 历史查询ViewModel
    /// </summary>
    public partial class HistoryQueryViewModel : ObservableObject
    {
        private readonly IMotorTestRepository _repo;
        private readonly IRegionManager _regionManager;
        private readonly ILogger _logger;

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
    }
}
