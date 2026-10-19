using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using Prism.Navigation.Regions;
using System.Collections.ObjectModel;
using System.Windows;
using Serilog;
using Thinger.DatabaseOperationlibrary;
using thinger.MotorTypeTestSystem.Models;
using thinger.MotorTypeTestSystem.Services;

namespace thinger.MotorTypeTestSystem.ViewModels
{
    public partial class CreateTestViewModel : ObservableObject, IConfirmNavigationRequest, IDestructible, IRegionMemberLifetime
    {
        private readonly IRegionManager _regionManager;

        private ILogger _Logger;
        private IMotorTestRepository _MotorTestRepository;

        //KeepAlive为true时，切换界面当前视图和ViewModel依然存在只是不显示，如果为false的话切换到别的界面
        //那么会导致直接将当前视图清理，并且调用Destroy()等方法。
        public bool KeepAlive => true;
        public string Title { get; } = "新建测试";

        /// <summary>
        /// 用来判断是否可以跳转到测试中心界面
        /// </summary>
        private bool _isNavigatingToExecution = false;

        #region 数据绑定模型
        /// <summary>
        /// 被测电机型号
        /// </summary>
        [ObservableProperty]
        private string _motorModel = string.Empty;

        /// <summary>
        /// 测试条码 / 出厂编号
        /// </summary>
        [ObservableProperty]
        private string _motorSerialNumber = string.Empty;

        /// <summary>
        /// 试验时间
        /// </summary>
        [ObservableProperty]
        private DateTime _testTime = DateTime.Now;

        /// <summary>
        /// 生产时间
        /// </summary>
        [ObservableProperty]
        private DateTime _productionTime = DateTime.Now;

        /// <summary>
        /// 额定功率
        /// </summary>
        [ObservableProperty]
        private double _ratedPower;

        /// <summary>
        /// 额定电压
        /// </summary>
        [ObservableProperty]
        private double _ratedVoltage;

        /// <summary>
        /// 额定电流
        /// </summary>
        [ObservableProperty]
        private double _ratedCurrent;

        /// <summary>
        /// 额定频率
        /// </summary>
        [ObservableProperty]
        private double _ratedFrequency;

        /// <summary>
        /// 额定转矩
        /// </summary>
        [ObservableProperty]
        private double _ratedTorque;

        /// <summary>
        /// 额定转速
        /// </summary>
        [ObservableProperty]
        private double _ratedSpeed;

        /// <summary>
        /// 标称效率
        /// </summary>
        [ObservableProperty]
        private double _nominalEfficiency;

        /// <summary>
        /// 标称功率因数
        /// </summary>
        [ObservableProperty]
        private double _ratedPowerFactor;

        /// <summary>
        /// 转动惯量
        /// </summary>
        [ObservableProperty]
        private double _rotationalInertia;

        /// <summary>
        /// 级数
        /// </summary>
        [ObservableProperty]
        private int _series;

        /// <summary>
        /// 工作制
        /// </summary>
        [ObservableProperty]
        private string _workSystem = "S1 (连续工作制)";
        public ObservableCollection<string> WorkSystemList { get; } = new ObservableCollection<string>
        {
            "S1 (连续工作制)",
            "S2 (短时工作制)",
            "S3 (断续周期工作制)",
            "S4 (包括起动的断续周期工作制)",
            "S5 (包括电制动的断续周期工作制)",
            "S6 (连续周期工作制)"
        };

        /// <summary>
        /// 连接方式
        /// </summary>
        [ObservableProperty]
        private string _connectionMethod = "星接";
        public ObservableCollection<string> ConnectionMethodList { get; } = new ObservableCollection<string>
        {
            "星接",
            "三角接",
        };

        /// <summary>
        /// 绝缘等级
        /// </summary>
        [ObservableProperty]
        private string _insulationClass = "B";
        public ObservableCollection<string> InsulationClassList { get; } = new ObservableCollection<string>
        {
            "A",
            "E",
            "B",
            "F",
            "H",
            "C"
        };

        /// <summary>
        /// 防护等级
        /// </summary>
        [ObservableProperty]
        private string _protectionLevel = "IP55";
        public ObservableCollection<string> ProtectionLevelList { get; } = new ObservableCollection<string>
        {
            "IP23",
            "IP44",
            "IP54",
            "IP55",
            "IP65",
            "IP67",
            "IP68"
        };

        /// <summary>
        /// 温升等级
        /// </summary>
        [ObservableProperty]
        private string _temperatureRiseClass = string.Empty;

        /// <summary>
        /// K1
        /// </summary>
        [ObservableProperty]
        private int _k1 = 235;
        public ObservableCollection<int> K1List { get; } = new ObservableCollection<int>
        {
            225,
            235
        };

        /// <summary>
        /// K2
        /// </summary>
        [ObservableProperty]
        private int _k2 = 235;
        public ObservableCollection<int> K2List { get; } = new ObservableCollection<int>
        {
            225,
            235
        };

        /// <summary>
        /// 试验方法
        /// </summary>
        [ObservableProperty]
        private string _selectedStandard = "GB/T 1032-2012 三相异步电动机试验方法";

        public ObservableCollection<string> StandardList { get; } = new ObservableCollection<string>
        {
            "GB/T 1032-2012 三相异步电动机试验方法",
            "GB/T 1029-2005 三相同步电机试验方法",
            "IEC 60034-2-1:2014 旋转电机试验方法"
        };

        #endregion

        public CreateTestViewModel(IRegionManager regionManager, IMotorTestRepository motorTestRepository, ILogger logger)
        {
            _regionManager = regionManager;
            _Logger = logger;
            _MotorTestRepository = motorTestRepository;
        }

        #region 创建测试按钮（将测试信息保存到数据库当中）
        [RelayCommand]
        private async Task StartTestAsync()
        {
            // ==================== 1. 前端验证 ====================
            if (string.IsNullOrWhiteSpace(MotorSerialNumber))
            {
                Growl.Warning("请输入电机序列号！");
                return;
            }
            if (string.IsNullOrWhiteSpace(MotorModel))
            {
                Growl.Warning("请输入被测电机型号！");
                return;
            }
            var record = new MotorTestRecord
            {
                Id = MotorTestRecord.GenerateId(this.MotorSerialNumber, this.TestTime),
                MotorModel = this.MotorModel,
                MotorSerialNumber = this.MotorSerialNumber,
                TestTime = this.TestTime,
                ProductionTime = this.ProductionTime,
                RatedPower = this.RatedPower,
                RatedVoltage = this.RatedVoltage,
                RatedCurrent = this.RatedCurrent,
                RatedFrequency = this.RatedFrequency,
                RatedTorque = this.RatedTorque,
                RatedSpeed = this.RatedSpeed,
                NominalEfficiency = this.NominalEfficiency,
                RatedPowerFactor = this.RatedPowerFactor,
                RotationalInertia = this.RotationalInertia,
                Series = this.Series,
                WorkSystem = this.WorkSystem,
                ConnectionMethod = this.ConnectionMethod,
                InsulationClass = this.InsulationClass,
                ProtectionLevel = this.ProtectionLevel,
                TemperatureRiseClass = this.TemperatureRiseClass,
                K1 = this.K1,
                K2 = this.K2,
                SelectedStandard = this.SelectedStandard,
                TestResultStatus = 0 // 0 = 进行中
            };
            bool success = await _MotorTestRepository.AddTestRecordAsync(record);
            if (success)
            {
                Growl.Success($"试验任务已建档成功！流水号：{record.Id}");

                // 建档成功后，清空序列号（保留型号和额定参数方便同批次电机连续测试，且点 × 关闭时不再误报未保存）
                MotorSerialNumber = string.Empty;

                var parameters = new NavigationParameters
                    {
                        { "CurrentTestRecord", record }
                    };

                //跳转到测试中心界面，并且将创建好的电机试验信息传递过去
                _regionManager.RequestNavigate("MainRegion", "TestExecutionView", parameters);
            }
        }
        #endregion

        #region 导航调用的方法
        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            _Logger.Information("进入新建测试界面");
        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            return true; // 每次都复用同一个新建测试实例
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            // 离开当前页时触发
        }
        #endregion

        #region 关闭拦截与清理 (重点补充)

        /// <summary>
        /// 当 Tab 被关闭（移除）或离开时触发拦截询问
        /// </summary>
        public void ConfirmNavigationRequest(NavigationContext navigationContext, Action<bool> continuationCallback)
        {
            // 1. 如果 navigationContext 不为 null，说明只是正常的 RequestNavigate 切换标签页（页面并没有关闭）
            // 直接放行，不需要弹窗打扰用户！
            if (navigationContext != null)
            {
                continuationCallback(true);
                return;
            }

            // 2. 如果 navigationContext 为 null，说明是用户在标签页上点击了右上角的 "×" 号要彻底关闭页面！
            if (!string.IsNullOrWhiteSpace(MotorSerialNumber))
            {
                var result = HandyControl.Controls.MessageBox.Show(
                    "测试信息尚未保存，确定要关闭此页面吗？",
                    "提示",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                {
                    // 传入 false，阻止关闭
                    continuationCallback(false);
                    return;
                }
            }

            // 允许关闭
            continuationCallback(true);
        }

        /// <summary>
        /// Tab 彻底被 Remove 移除时触发，用于释放底层资源
        /// </summary>
        public void Destroy()
        {
            _Logger.Information("新建测试界面关闭");
        }

        #endregion
    }
}
