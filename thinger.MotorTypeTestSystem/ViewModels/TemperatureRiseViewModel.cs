using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using thinger.MotorTypeTestSystem.Models;
using thinger.MotorTypeTestSystem.Models.TestHelpers;
using thinger.MotorTypeTestSystem.Services;
using thinger.MotorTypeTestSystem.Views.Dialogs;

namespace thinger.MotorTypeTestSystem.ViewModels
{
    public partial class TemperatureRiseViewModel : ObservableObject, ISubTestViewModel
    {
        private readonly ITempChannelService _tempChannelService;
        public ITempChannelService TempChannelService => _tempChannelService;
        private readonly TagPoolService _tagPool;
        private readonly ILogger _logger;
        private readonly IMotorTestRepository _repo;
        private readonly IResistanceMeterService _resistanceMeterService;

        private readonly DispatcherTimer _timer;
        private int _elapsedSeconds = 0;//用来记录试验的时间秒数
        private readonly System.Diagnostics.Stopwatch _stopwatch = new();

        /// <summary>
        /// GB/T 1032 各通道滑动时间窗口历史温度采样队列: ChannelTagName -> Queue<(Second, Temp)>
        /// 用于计算连续 1 小时 (或滑动窗口) 的各测温部位独立温升斜率
        /// </summary>
        private readonly Dictionary<string, Queue<(int Second, double Temp)>> _channelHistory = new();

        #region 给 View 调用的事件委托
        public event Action? RequestClearChart;
        public event Action<string, DateTime, double>? RequestAddPoint;
        public event Func<string, bool>? RequestExportChartImage;
        public event Func<string, bool>? RequestSaveChartData;
        public event Func<string, bool>? RequestLoadChartData;

        //通知界面在画布上画线和清空画布
        public event Action<List<CoolingPoint>, double, double, int, double>? RequestDrawCoolingPlot;
        public event Action? RequestClearCoolingPlot;
        #endregion

        #region 停机冷却后倒推热电阻的属性
        //停机采集热电阻及时间参数的类的集合
        public ObservableCollection<CoolingPoint> ManualCoolingPoints { get; } = new();
        // 拟合详情说明文本（初始为空，只有在点击拟合后才显示算出的 R0 和 R²）
        [ObservableProperty]
        private string _coolingFitDetails = string.Empty;

        /// <summary>
        ///外推目标时间点（秒）：默认为 0 秒（国标断电瞬间），支持用户设为 20、30 等
        /// </summary>
        [ObservableProperty]
        private int _coolingTargetSeconds = 0;
        #endregion

        #region Observable 属性
        [ObservableProperty]
        private MotorTestRecord? _motorRecord;

        [ObservableProperty]
        private TemperatureRiseRecord _tempRecord = new();

        [ObservableProperty]
        private string _elapsedTimeString = "00:00:00";

        [ObservableProperty]
        private string _thermalStatusText = "未开始";

        /// <summary>
        /// GB/T 1032-2023 全机各测点是否均已达到热稳定状态 (所有受试部件温升速率均 ≤ 1.0 K/h)
        /// </summary>
        [ObservableProperty]
        private bool _isThermalStabilized = false;

        /// <summary>
        /// 当前各测点中最大的温升变化速率 (K/h)
        /// </summary>
        [ObservableProperty]
        private double _maxHourlyRate = 0.0;

        /// <summary>
        /// 当前温升速率最大的测点名称 (如 "驱动端前轴承")
        /// </summary>
        [ObservableProperty]
        private string _maxRatePointName = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartTestCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopTestCommand))]
        [NotifyCanExecuteChangedFor(nameof(OpenChannelConfigCommand))]
        [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
        private bool _isTesting = false;

        /// <summary>
        /// 是否开启每隔 30 分钟自动记录工况 (可由界面随时一键开启/停用)
        /// </summary>
        [ObservableProperty]
        private bool _isAutoSampleEnabled = false;

        /// <summary>
        /// GB/T 1032-2023 试验过程工况记录明细列表
        /// </summary>
        public ObservableCollection<TempRiseProcessItem> ProcessRecords { get; } = new();

        /// <summary>
        /// 试验是否处于暂停状态 (仅控制界面切换黄色暂停还是绿色继续)
        /// </summary>
        [ObservableProperty]
        private bool _isPaused = false;
        #endregion

        public TemperatureRiseViewModel(
            ITempChannelService tempChannelService,
            TagPoolService tagPool,
            ILogger logger,
            IMotorTestRepository repo,
            IResistanceMeterService resistanceMeterService)
        {
            _tempChannelService = tempChannelService;
            _tagPool = tagPool;
            _logger = logger;
            _repo = repo;
            _resistanceMeterService = resistanceMeterService;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += OnTimerTick;          
        }

        #region 页面载入与自动关联
        /// <summary>
        /// 切换到本页面时自动执行：加载历史或自动拉取冷态数据与绝缘限值
        /// </summary>
        public async void InitializeTestRecord(MotorTestRecord mainRecord)
        {
            if (mainRecord == null) return;
            MotorRecord = mainRecord;

            try
            {
                // 1. 先查数据库：这台电机是否已经做过温升试验？
                var existing = await _repo.GetTemperatureRiseByRecordIdAsync(mainRecord.Id);
                if (existing != null)
                {
                    TempRecord = existing;

                    // 反序列化历史过程工况快照记录
                    if (!string.IsNullOrWhiteSpace(existing.SnapshotsJson))
                    {
                        try
                        {
                            var list = JsonSerializer.Deserialize<List<TempRiseProcessItem>>(existing.SnapshotsJson);
                            if (list != null)
                            {
                                ProcessRecords.Clear();
                                foreach (var item in list) ProcessRecords.Add(item);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning(ex, "解析历史温升过程工况数据失败");
                        }
                    }

                    //自动回放曲线代码
                    if (!string.IsNullOrWhiteSpace(existing.CurveDataFilePath) && System.IO.File.Exists(existing.CurveDataFilePath))
                    {
                        bool loaded = RequestLoadChartData?.Invoke(existing.CurveDataFilePath) ?? false;
                        if (loaded)
                        {
                            _logger.Information($"已自动加载并重绘电机 [{mainRecord.Id}] 的历史温升曲线");
                        }
                    }

                    _logger.Information($"已加载电机 [{mainRecord.Id}] 的历史温升试验数据");
                    return;
                }

                // 2. 若是新试验，初始化实体
                TempRecord = new TemperatureRiseRecord
                {
                    RecordId = mainRecord.Id
                };

                // 3. 🌟 自动化亮点：自动调出前道【冷态直流电阻】的测试数据，免去操作员二次手工录入！
                var dcRecord = await _repo.GetDcResistanceByRecordIdAsync(mainRecord.Id);
                if (dcRecord != null && dcRecord.AverageResistance > 0)
                {
                    // 从前道拉取冷态平均线电阻后，按接法统一折算为【冷态相电阻 R1】：
                    string connection = MotorRecord?.ConnectionMethod?.Trim() ?? "星接";
                    if (connection.Contains("角") || connection.Contains("△") || connection.ToUpper().Contains("DELTA"))
                    {
                        TempRecord.ColdResistance = Math.Round(dcRecord.AverageResistance * 1.5, 5);
                    }
                    else
                    {
                        TempRecord.ColdResistance = Math.Round(dcRecord.AverageResistance * 0.5, 5);
                    }

                    TempRecord.ColdTemp = dcRecord.WindingTemp;
                    Growl.Info($"已自动载入该电机的冷态电阻数据 (R1: {TempRecord.ColdResistance} Ω, θ1: {TempRecord.ColdTemp} ℃)");
                }

                // 4. 根据电机铭牌绝缘等级自动设定国标限值 (如 F级 100K，B级 80K，H级 125K)
                TempRecord.AllowedTempRise = GetAllowedTempRiseByClass(mainRecord.InsulationClass);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "初始化温升试验基准数据失败");
            }
        }

        private double GetAllowedTempRiseByClass(string? insClass)
        {
            return insClass?.Trim().ToUpper() switch
            {
                "A" => 60.0,
                "E" => 75.0,
                "B" => 80.0,
                "F" => 100.0,
                "H" => 125.0,
                _ => 100.0
            };
        }
        #endregion

        #region 试验启停控制
        [RelayCommand(CanExecute = nameof(CanStartTest))]
        private void StartTest()
        {
            IsPaused = false;
            if (_elapsedSeconds > 0)
            {
                if (HandyControl.Controls.MessageBox.Show(
                    "检测到当前已有试验记录。\n重新开始试验将清空上一轮所有曲线与计时数据并重新归零，是否确定继续？",
                    "确认重新开始", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            _elapsedSeconds = 0;
            ElapsedTimeString = "00:00:00";
            _stopwatch.Restart(); //秒表归零并立刻开始计时
            ThermalStatusText = "升温中 (初期)";
            IsThermalStabilized = false;
            MaxHourlyRate = 0.0;
            MaxRatePointName = string.Empty;
            _channelHistory.Clear();
            RequestClearChart?.Invoke();

            // 清空工况明细表并记录初始 t=0 状态
            ProcessRecords.Clear();
            if (IsAutoSampleEnabled)
            {
                CaptureProcessRecord();
            }
            IsAutoSampleEnabled = true;
            IsTesting = true;
            _timer.Start();

            SampleCurrentTemperatures();
            Growl.Success("温升试验已启动并归零！");
        }

        private bool CanStartTest() => !IsTesting;

        [RelayCommand(CanExecute = nameof(CanStopTest))]
        private void StopTest()
        {
            IsPaused = false;
            if (HandyControl.Controls.MessageBox.Show(
                "确定要停止当前的温升试验吗？停止后将切断计时，并抓取当前温度准备测量热态电阻。",
                "停止确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }            
            _timer.Stop();
            _stopwatch.Stop(); //秒表停止
            IsTesting = false;
            ThermalStatusText = "试验已停止，请抢测热态电阻";
            TempRecord.TestDuration = ElapsedTimeString;

            // 自动抓取停机瞬间的环境温度和轴承最高温度！
            CaptureShutdownTemperatures();
            IsAutoSampleEnabled = false;
            Growl.Warning("电机已停机！请在 30 秒内快速测入热态直流电阻！");
            _logger.Information($"温升试验已停机，历时：{ElapsedTimeString}");
        }
        private bool CanStopTest() => IsTesting;

        /// <summary>
        /// 暂停试验 / 继续试验 切换命令
        /// </summary>
        /// <returns></returns>
        [RelayCommand(CanExecute = nameof(CanTogglePause))]
        private void TogglePause()
        {
            if (!IsTesting) return;
            if (!IsPaused)
            {
                // ------------------ 执行【暂停】 ------------------
                IsPaused = true;
                _timer.Stop(); // 停止定时器：计时走秒、5秒画线打点、30分钟自动采集全部冻结
                _stopwatch.Stop(); //秒表暂停！暂停期间自动不计秒！
                ThermalStatusText = "⏸ 试验已暂停 (测控与计时已冻结)";
                Growl.Warning("温升试验已暂停！处理完现场后请尽快点击【继续试验】。");
                _logger.Information($"温升试验已在历时 {ElapsedTimeString} 处手动暂停");
            }
            else
            {
                // ------------------ 执行【恢复继续】 ------------------
                IsPaused = false;
                _timer.Start(); // 重新启动定时器，无缝接续
                _stopwatch.Start(); // 🌟 秒表继续走！自动接续刚才的秒数！
                ThermalStatusText = "升温中 (试验已恢复)";
                Growl.Success("温升试验已恢复，继续测控计时！");
                _logger.Information($"温升试验在历时 {ElapsedTimeString} 处恢复继续");
            }
        }
        // 只要在试验中（无论是运行中还是暂停中），该按钮都可以点击
        private bool CanTogglePause() => IsTesting;


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

        #region 热态电阻微欧计采入
        /// <summary>
        /// 点击【采入】按钮采集热态各相电阻 (phase: "UV", "VW", "WU")
        /// </summary>
        [RelayCommand]
        private async Task SampleHotResistance(string phase)
        {
            if (TempRecord == null || string.IsNullOrWhiteSpace(phase)) return;

            var (success, val, msg) = await _resistanceMeterService.ReadResistanceAsync();
            if (!success)
            {
                Growl.Warning(msg);
                return;
            }

            switch (phase.ToUpper())
            {
                case "UV":
                    TempRecord.HotRuv = val;
                    Growl.Success($"已采入热态 U-V 阻值：{val} Ω");
                    break;
                case "VW":
                    TempRecord.HotRvw = val;
                    Growl.Success($"已采入热态 V-W 阻值：{val} Ω");
                    break;
                case "WU":
                    TempRecord.HotRwu = val;
                    Growl.Success($"已采入热态 W-U 阻值：{val} Ω");
                    break;
            }

            // 自动计算三相热态平均电阻 R2
            if (TempRecord.HotRuv > 0 && TempRecord.HotRvw > 0 && TempRecord.HotRwu > 0)
            {
                double avgLine = (TempRecord.HotRuv + TempRecord.HotRvw + TempRecord.HotRwu) / 3.0;
                string connection = MotorRecord?.ConnectionMethod?.Trim() ?? "星接";
                if (connection.Contains("角") || connection.Contains("△") || connection.ToUpper().Contains("DELTA"))
                {
                    TempRecord.HotResistance = Math.Round(avgLine * 1.5, 5);
                }
                else
                {
                    TempRecord.HotResistance = Math.Round(avgLine * 0.5, 5);
                }
            }
            else
            {
                TempRecord.HotResistance = val;
            }

            CalculateResults();
        }
        #endregion

        #region 国家标准自动核算与判定
        /// <summary>
        /// 执行国家标准 GB/T 1032 电阻法温升换算
        /// </summary>
        [RelayCommand]
        public void CalculateResults()
        {
            //无论怎么输入的，只要三相线电阻大于 0，立刻自动折算出相电阻 HotResistance！
            if (TempRecord.HotRuv > 0 && TempRecord.HotRvw > 0 && TempRecord.HotRwu > 0)
            {
                double avgLine = (TempRecord.HotRuv + TempRecord.HotRvw + TempRecord.HotRwu) / 3.0;
                string connection = MotorRecord?.ConnectionMethod?.Trim() ?? "星接";
                if (connection.Contains("角") || connection.Contains("△") || connection.ToUpper().Contains("DELTA"))
                {
                    TempRecord.HotResistance = Math.Round(avgLine * 1.5, 5);
                }
                else
                {
                    TempRecord.HotResistance = Math.Round(avgLine * 0.5, 5);
                }
            }

            if (TempRecord.ColdResistance <= 0 || TempRecord.HotResistance <= 0)
            {
                return;
            }

            // 1. 电阻法计算绕组热态实际内部温度 θ2：
            // 公式：θ2 = (R2 / R1) * (K1 + t1) - K1
            double k1 = MotorRecord?.K1 > 0 ? MotorRecord.K1 : 235.0;
            double r1 = TempRecord.ColdResistance;
            double r2 = TempRecord.HotResistance;
            double t1 = TempRecord.ColdTemp;
            double t2 = TempRecord.ShutdownAmbientTemp;

            double theta2 = (r2 / r1) * (k1 + t1) - k1;
            TempRecord.HotWindingTemp = Math.Round(theta2, 2);

            // 2. 实际考核温升：Δθ = 热态温度 θ2 - 停机环境温度 t2 (数学上完全等价于国标展开式，t2也就是θb)
            double deltaTheta = theta2 - t2;
            TempRecord.TemperatureRise = Math.Round(deltaTheta, 2);

            // 3. 合格判定裁决 (温升不超过允许限值，且轴承不超过限值)
            bool windingOk = TempRecord.TemperatureRise <= TempRecord.AllowedTempRise;
            bool bearingOk = (TempRecord.MaxBearingTemp <= 0) || (TempRecord.MaxBearingTemp <= TempRecord.AllowedBearingTemp);

            TempRecord.IsQualified = windingOk && bearingOk;
        }
        #endregion

        #region 保存试验结果入库
        [RelayCommand]
        public async Task SaveTestRecord()
        {
            if (MotorRecord == null || string.IsNullOrWhiteSpace(TempRecord.RecordId))
            {
                Growl.Warning("未关联有效的试验任务，无法保存！");
                return;
            }

            CalculateResults();

            if (TempRecord.ColdResistance <= 0 || TempRecord.HotResistance <= 0)
            {
                Growl.Warning("请先完善冷态电阻或热态电阻数据后再保存！");
                return;
            }

            try
            {
                TempRecord.TestTime = DateTime.Now;

                //AppDomain.CurrentDomain.BaseDirectory指的是该程序.exe文件所在的路径,curvesFolder代表曲线文件夹
                string curvesFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Data","Curves");
                if (!System.IO.Directory.Exists(curvesFolder))
                {
                    System.IO.Directory.CreateDirectory(curvesFolder);
                }
                //curveFilePath代表曲线文件名
                string curveFilePath = System.IO.Path.Combine(curvesFolder,$"TempRise_{TempRecord.RecordId}.csv");
                bool saved = RequestSaveChartData?.Invoke(curveFilePath)??false;
                if (saved)
                {
                    TempRecord.CurveDataFilePath = curveFilePath;
                    _logger.Information($"温升曲线数据已自动存盘：{curveFilePath}");
                }

                // 自动保存 PNG 高清截图图片
                string imageFilePath = System.IO.Path.Combine(curvesFolder, $"TempRise_{TempRecord.RecordId}.png");
                bool imageSaved = RequestExportChartImage?.Invoke(imageFilePath) ?? false;
                if (imageSaved)
                {
                    TempRecord.CurveImagePath = imageFilePath;
                    _logger.Information($"温升曲线截图已自动存盘：{imageFilePath}");
                }

                // 序列化 GB/T 1032 过程工况记录明细为 JSON 字符串
                if (ProcessRecords.Count > 0)
                {
                    TempRecord.SnapshotsJson = JsonSerializer.Serialize(ProcessRecords);
                }

                // 保存温升数据记录到 MySQL 数据库
                bool success = await _repo.SaveTemperatureRiseAsync(TempRecord);
                if (success)
                {
                    Growl.Success("温升试验全部数据已成功归档保存！");
                    _logger.Information($"电机 [{TempRecord.RecordId}] 温升试验数据已保存入库");
                }
                else
                {
                    Growl.Error("保存温升数据失败，请检查数据库连接！");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "保存温升试验数据发生异常");
                Growl.Fatal($"保存异常：{ex.Message}");
            }
        }
        #endregion

        #region GB/T 1032 过程工况数据采集 (手动采集与自动记录)
        /// <summary>
        /// 操作员点击【手动采集】按钮，抓取当前工况打入记录表
        /// </summary>
        [RelayCommand]
        public void ManualSampleProcessRecord()
        {
            var item = CaptureProcessRecord();
            Growl.Success($"已手动采入第 {item.Index} 组过程工况数据 (历时 {item.ElapsedTimeString})！");
            _logger.Information($"手动采入过程工况数据：序号={item.Index}, 历时={item.ElapsedTimeString}, 机壳/绕组={item.MotorTemp}℃, 环境={item.AmbientTemp}℃");
        }

        /// <summary>
        /// 删除指定的过程工况记录行，并自动重排序号
        /// </summary>
        [RelayCommand]
        public void DeleteProcessRecord(TempRiseProcessItem? item)
        {
            if (item == null) return;
            int oldIdx = item.Index;
            ProcessRecords.Remove(item);
            // 重新按 1, 2, 3... 刷新序号
            for (int i = 0; i < ProcessRecords.Count; i++)
            {
                ProcessRecords[i].Index = i + 1;
            }
            Growl.Info($"已删除第 {oldIdx} 组工况记录");
            _logger.Information($"操作员删除了第 {oldIdx} 组工况记录，剩余记录数：{ProcessRecords.Count}");
        }

        /// <summary>
        /// 清空所有过程工况记录行
        /// </summary>
        [RelayCommand]
        public void ClearProcessRecords()
        {
            if (ProcessRecords.Count == 0) return;
            if (HandyControl.Controls.MessageBox.Show(
                "确定要清空所有已采入的过程工况数据吗？",
                "清空确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                ProcessRecords.Clear();
                Growl.Success("已清空所有过程工况记录");
            }
        }

        /// <summary>
        /// 抓取当前所有传感器与电量快照，组装为明细行并加入列表
        /// </summary>
        public TempRiseProcessItem CaptureProcessRecord()
        {
            var channelTemps = new Dictionary<string, double>();
            var channelTempsByIndex = new Dictionary<int, double>();
            double ambient = 25.0;
            double motorTemp = 25.0;
            double maxBearing = 0.0;

            // 1. 遍历当前配置的所有启用通道，测了什么就抓取什么
            foreach (var ch in _tempChannelService.Channels)
            {
                if (!ch.IsEnabled) continue;

                double val = 25.0;
                var tag = _tagPool[ch.TagName];
                if (tag?.Value != null && double.TryParse(tag.Value.ToString(), out double parsed) && parsed > 0)
                {
                    val = parsed;
                }
                else
                {
                    if (ch.IsAmbient || ch.PointType == "环境基准")
                    {
                        val = 25.0;
                    }
                    else
                    {
                        double finalRise = 45.0 + (ch.ChannelIndex * 4.0);
                        double tau = 1200.0 + (ch.ChannelIndex * 300.0);
                        val = Math.Round(25.0 + finalRise * (1.0 - Math.Exp(-_elapsedSeconds / tau)), 1);
                    }
                }

                string displayName = string.IsNullOrWhiteSpace(ch.PointName) ? ch.ChannelDisplay : $"{ch.ChannelDisplay}({ch.PointName})";
                channelTemps[displayName] = Math.Round(val, 1);
                channelTempsByIndex[ch.ChannelIndex] = Math.Round(val, 1);
                if (ch.IsAmbient) ambient = val;
                if (ch.PointType == "轴承" && val > maxBearing) maxBearing = val;
                if ((ch.PointType == "绕组" || ch.PointType == "机壳") && val > motorTemp) motorTemp = val;
            }

            // 2. 抓取三相电压与电流 (优先读取硬件变量池，若无则使用名牌额定值模拟)
            double ratedU = MotorRecord?.RatedVoltage > 0 ? MotorRecord.RatedVoltage : 380.0;
            double uab = ReadTagOrFallback("IO.Voltage_Uab", ratedU);
            double ubc = ReadTagOrFallback("IO.Voltage_Ubc", ratedU);
            double uca = ReadTagOrFallback("IO.Voltage_Uca", ratedU);
            double uAvg = ReadTagOrFallback("U_avg", Math.Round((uab + ubc + uca) / 3.0, 1));

            double ratedI = MotorRecord?.RatedCurrent > 0 ? MotorRecord.RatedCurrent : 15.2;
            double ia = ReadTagOrFallback("IO.Current_Ia", ratedI);
            double ib = ReadTagOrFallback("IO.Current_Ib", ratedI);
            double ic = ReadTagOrFallback("IO.Current_Ic", ratedI);
            double iAvg = ReadTagOrFallback("I_avg", Math.Round((ia + ib + ic) / 3.0, 2));

            // 电气与机械参数读取 (优先读取变量池，未连接硬件时根据名牌平滑模拟)
            double power = ReadTagOrFallback("P_active", MotorRecord?.RatedPower > 0 ? MotorRecord.RatedPower : 7.5);
            double powerFactor = ReadTagOrFallback("PowerFactor", MotorRecord?.RatedPowerFactor > 0 ? MotorRecord.RatedPowerFactor : 0.86);
            double freq = ReadTagOrFallback("Frequency", MotorRecord?.RatedFrequency > 0 ? MotorRecord.RatedFrequency : 50.0);
            double speed = ReadTagOrFallback("Speed", MotorRecord?.RatedSpeed > 0 ? MotorRecord.RatedSpeed : 1450.0);
            double torque = ReadTagOrFallback("Torque", MotorRecord?.RatedTorque > 0 ? MotorRecord.RatedTorque : (power > 0 && speed > 0 ? Math.Round(9550.0 * power / speed, 2) : 49.4));

            var item = new TempRiseProcessItem
            {
                Index = ProcessRecords.Count + 1,
                ElapsedTimeString = ElapsedTimeString,
                Timestamp = DateTime.Now,

                VoltageUab = Math.Round(uab, 1),
                VoltageUbc = Math.Round(ubc, 1),
                VoltageUca = Math.Round(uca, 1),
                Voltage = Math.Round(uAvg, 1),

                CurrentIa = Math.Round(ia, 2),
                CurrentIb = Math.Round(ib, 2),
                CurrentIc = Math.Round(ic, 2),
                Current = Math.Round(iAvg, 2),

                ActivePower = Math.Round(power, 2),
                PowerFactor = Math.Round(powerFactor, 3),
                Frequency = Math.Round(freq, 2),
                Speed = Math.Round(speed, 0),
                Torque = Math.Round(torque, 2),

                AmbientTemp = Math.Round(ambient, 1),
                MotorTemp = Math.Round(motorTemp, 1),
                BearingTemp = Math.Round(maxBearing, 1),

                ChannelTemps = channelTemps,
                ChannelTempsByIndex = channelTempsByIndex
            };

            ProcessRecords.Add(item);
            return item;
        }

        private double ReadTagOrFallback(string tagName, double fallback)
        {
            var tag = _tagPool[tagName];
            if (tag?.Value != null && double.TryParse(tag.Value.ToString(), out double parsed) && parsed > 0)
            {
                return parsed;
            }
            return fallback;
        }
        #endregion

        #region 内部辅助方法
        private void CaptureShutdownTemperatures()
        {
            double maxBearing = 0;
            double ambient = 25.0;
            foreach (var ch in _tempChannelService.Channels)
            {
                if (!ch.IsEnabled) continue;
                var tag = _tagPool[ch.TagName];
                if (tag?.Value != null && double.TryParse(tag.Value.ToString(), out double val))
                {
                    if (ch.IsAmbient) ambient = val;
                    if (ch.PointType == "轴承" && val > maxBearing) maxBearing = val;
                }
            }
            TempRecord.ShutdownAmbientTemp = ambient;
            TempRecord.MaxBearingTemp = maxBearing;
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            int previousSeconds = _elapsedSeconds;
            //直接从高精度秒表读取当前已运行的流逝时间！
            TimeSpan elapsed = _stopwatch.Elapsed;
            _elapsedSeconds = (int)elapsed.TotalSeconds;
            ElapsedTimeString = elapsed.ToString(@"hh\:mm\:ss");

            // 1. 每 5 秒打一个曲线点（跨越 5 秒周期即打点，熄屏唤醒也不漏点）
            if (_elapsedSeconds / 5 > previousSeconds / 5)
            {
                SampleCurrentTemperatures();
            }
            // 2. GB/T 1032 过程工况自动记录：每满 30 分钟 (1800 秒) 自动记录一组
            if (IsAutoSampleEnabled && _elapsedSeconds > 0 && (_elapsedSeconds / 1800 > previousSeconds / 1800))
            {
                var autoItem = CaptureProcessRecord();
                Growl.Info($"[自动采集] 已记录第 {autoItem.Index} 组 30 分钟过程工况数据");
            }
        }

        private void SampleCurrentTemperatures()
        {
            DateTime now = DateTime.Now;
            foreach (var channel in _tempChannelService.Channels)
            {
                if (!channel.IsEnabled) continue;
                var tag = _tagPool[channel.TagName];
                double tempValue = 0.0;
                if (tag?.Value != null && double.TryParse(tag.Value.ToString(), out double parsed) && parsed > 0)
                {
                    tempValue = parsed;
                }
                else
                {
                    // 仿真平滑物理饱和曲线：T(t) = T_amb + ΔT_∞ * (1 - exp(-t/τ))
                    // 各测点热时间常数不同：绕组升温最快(τ小)，铁芯次之，轴承升温最滞后(τ大)
                    double finalRise = 45.0 + (channel.ChannelIndex * 4.0);
                    double tau = 1200.0 + (channel.ChannelIndex * 300.0);
                    tempValue = Math.Round(25.0 + finalRise * (1.0 - Math.Exp(-_elapsedSeconds / tau)), 2);
                }
                RequestAddPoint?.Invoke(channel.PointName, now, tempValue);

                // 记录非环境考核测点的历史队列（保留最多 1 小时滑动窗口）
                if (!channel.IsAmbient)
                {
                    if (!_channelHistory.TryGetValue(channel.TagName, out var history))
                    {
                        history = new Queue<(int Second, double Temp)>();
                        _channelHistory[channel.TagName] = history;
                    }
                    history.Enqueue((_elapsedSeconds, tempValue));
                    while (history.Count > 0 && (_elapsedSeconds - history.Peek().Second) > 3600)
                    {
                        history.Dequeue();
                    }
                }
            }

            // 🌟 核心：执行 GB/T 1032 各部位多通道热稳定严密判决
            EvaluateThermalEquilibrium();
        }

        /// <summary>
        /// GB/T 1032-2023 / GB/T 755 严密各测点热稳定判据引擎：
        /// 独立计算所有启用受试部件（绕组、铁芯、前轴承、后轴承、机壳等，排除环境基准）的独立温升变化速率。
        /// 只有当全机所有测点中的最大速率 v_max ≤ 1.0 K/h 时，判定达到国家标准热稳定状态！
        /// </summary>
        private void EvaluateThermalEquilibrium()
        {
            // 试验开始前 10 分钟为初始升温阶段，各部件温升速率极大
            if (_elapsedSeconds < 600)
            {
                IsThermalStabilized = false;
                ThermalStatusText = $"升温中 (观测中 {_elapsedSeconds / 60}m/10m)";
                return;
            }

            double maxRate = 0.0;
            string maxPointName = string.Empty;
            int validChannels = 0;

            foreach (var channel in _tempChannelService.Channels)
            {
                if (!channel.IsEnabled || channel.IsAmbient) continue;

                if (_channelHistory.TryGetValue(channel.TagName, out var history) && history.Count >= 2)
                {
                    var oldest = history.Peek();
                    var newest = history.Last();

                    double dtHours = (newest.Second - oldest.Second) / 3600.0;
                    // 至少跨越 5 分钟 (300 秒) 的时间差才计算斜率，平滑滤除瞬态通信抖动与底噪
                    if (dtHours >= (300.0 / 3600.0))
                    {
                        double dTemp = newest.Temp - oldest.Temp;
                        double rate = Math.Round(dTemp / dtHours, 2);
                        if (rate < 0) rate = 0; // 冷却波动或负漂移视为已无温升

                        validChannels++;
                        if (rate > maxRate)
                        {
                            maxRate = rate;
                            maxPointName = string.IsNullOrWhiteSpace(channel.PointName) ? channel.ChannelDisplay : channel.PointName;
                        }
                    }
                }
            }

            if (validChannels == 0)
            {
                ThermalStatusText = "升温中 (历史数据累积中)";
                return;
            }

            MaxHourlyRate = maxRate;
            MaxRatePointName = maxPointName;

            // 🌟 GB/T 1032 国标严密准则：全机所有测温部位最大速率 ≤ 1.0 K/h
            if (maxRate <= 1.0)
            {
                if (!IsThermalStabilized)
                {
                    IsThermalStabilized = true;
                    Growl.Success($"🔥 全机各部位均已达到热稳定！\n各通道最大变化速率: {maxRate:F2} K/h ≤ 1.0 K/h\n建议停止试验，抢测热态电阻！");
                    _logger.Information($"[热稳定达成] 所有测点温升速率均 ≤ 1.0 K/h，最大部位【{maxPointName}】为 {maxRate:F2} K/h");
                }
                ThermalStatusText = $"🔥 各部位均达热稳定 (最大: {maxRate:F2} K/h ≤ 1.0 K/h，建议停机)";
            }
            else
            {
                IsThermalStabilized = false;
                ThermalStatusText = $"升温中 (【{maxPointName}】速率: {maxRate:F1} K/h > 1.0 K/h)";
            }
        }
        #endregion

        #region 停机冷却外推手工录入命令
        /// <summary>
        /// 1. 添加一行数据：点一次加一行，序号自动递增 1, 2, 3...
        /// </summary>
        [RelayCommand]
        public void AddManualCoolingPoint()
        {
            ManualCoolingPoints.Add(new CoolingPoint
            {
                Index = ManualCoolingPoints.Count + 1,
                Seconds = 0,
                Resistance = 0
            });
        }

        /// <summary>
        /// 2. 删除指定行，并自动重新从 1, 2, 3... 刷新序号
        /// </summary>
        [RelayCommand]
        public void DeleteManualCoolingPoint(CoolingPoint? pt)
        {
            if (pt == null) return;
            ManualCoolingPoints.Remove(pt);
            for (int i = 0; i < ManualCoolingPoints.Count; i++)
            {
                ManualCoolingPoints[i].Index = i + 1;
            }
        }

        /// <summary>
        /// 3. 清空表格与图表
        /// </summary>
        [RelayCommand]
        public void ClearManualCoolingPoints()
        {
            ManualCoolingPoints.Clear();
            CoolingFitDetails = string.Empty;
            RequestClearCoolingPlot?.Invoke();
        }

        /// <summary>
        /// 4. 🌟 一键拟合外推：画右侧曲线、算 R0、赋给热态电阻并触发温升核算！
        /// </summary>
        [RelayCommand]
        public void FitAndApplyCoolingResistance()
        {
            // 过滤出真正填了数字的有效行（秒数 > 0 且 电阻 > 0）
            var validPoints = ManualCoolingPoints
                .Where(p => p.Seconds > 0 && p.Resistance > 0)
                .OrderBy(p => p.Seconds)
                .ToList();
            if (validPoints.Count < 2)
            {
                Growl.Warning("请至少在表格中填入 2 组有效的【停机秒数】和【实测电阻】！");
                return;
            }
            // 最小二乘法计算
            var (r0, slope, r2) = CoolingCurveHelper.CalculateExtrapolation(validPoints);
            if (r0 <= 0)
            {
                Growl.Error("外推计算失败，请检查输入的数据！");
                return;
            }
            //根据用户设定的目标秒数计算最终阻值（t=0 即为 R0，t=30 即为 R0 + slope * 30）
            int targetSec = Math.Max(0, CoolingTargetSeconds);
            double targetR = Math.Round(r0 + slope * targetSec, 5);
            CoolingFitDetails = targetSec == 0
                ? $"★ 断电瞬间 R(0s) = {r0:F5} Ω | 降温斜率 k = {Math.Abs(slope):F6} Ω/s | 拟合优度 R² = {r2:F4}"
                : $"★ 外推到 {targetSec}s 阻值 R({targetSec}s) = {targetR:F5} Ω (R₀={r0:F5}Ω) | 斜率 k = {Math.Abs(slope):F6} Ω/s | R² = {r2:F4}";
            // 通知 View 绘制散点、回归线并标记目标点
            RequestDrawCoolingPlot?.Invoke(validPoints, r0, slope, targetSec, targetR);

            // 自动把外推出来的线电阻赋给热态输入框
            TempRecord.HotRuv = r0;
            TempRecord.HotRvw = r0;
            TempRecord.HotRwu = r0;
            // 自动触发温升核算与合格判定！
            CalculateResults();
            Growl.Success($"🔥 曲线绘制成功！\n断电瞬间阻值 R₀ = {r0:F5} Ω 已自动填入热态并完成温升核算！");
            _logger.Information($"手工录入外推计算成功：R0 = {r0:F5} Ω, 斜率 k = {slope:F6}, R² = {r2:F4}");
        }
        #endregion
    }
}
