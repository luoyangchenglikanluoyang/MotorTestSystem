using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.Win32;
using ScottPlot;
using ScottPlot.Plottables;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace thinger.MotorTypeTestSystem.Controls
{
    /// <summary>
    /// IndustrialTrendChart.xaml 的交互逻辑
    /// </summary>
    public partial class IndustrialTrendChart : UserControl
    {
        // 1. 存储已创建的通道曲线字典 (通道名称 -> 曲线DataLogger)
        private readonly Dictionary<string, DataLogger> _channelLoggers = new();

        // 存储已经创建的 Y 轴字典 (轴名称如 "温度 (°C)", "转速 (rpm)" -> Y轴对象)
        private readonly Dictionary<string, ScottPlot.IYAxis> _yAxes = new();

        // 2. 引入 ScottPlot 官方权威的 20 色调色板
        private readonly ScottPlot.Palettes.Category20 _palette20 = new();

        // 记录当前已经分发了多少个通道颜色
        private int _channelCount = 0;

        // 🌟 存储交互式复选框图例数据源
        private readonly ObservableCollection<ChartChannelItem> _channelItems = new();

        // 🌟 存储鼠标悬浮十字准星
        private ScottPlot.Plottables.Crosshair? _crosshair;
        private bool _isInitialized = false;
        private bool _isRenderHookRegistered = false;

        #region 依赖属性 (外部直接可配标题和Y轴标签)
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }
        public static readonly DependencyProperty TitleProperty =
           DependencyProperty.Register("Title", typeof(string), typeof(IndustrialTrendChart),
               new PropertyMetadata("📈 实时多通道曲线", (d, e) => ((IndustrialTrendChart)d).TxtTitle.Text = e.NewValue?.ToString()));

        public string YAxisLabel
        {
            get => (string)GetValue(YAxisLabelProperty);
            set => SetValue(YAxisLabelProperty, value);
        }
        // Using a DependencyProperty as the backing store for MyProperty.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty YAxisLabelProperty =
            DependencyProperty.Register("YAxisLabel", typeof(string), typeof(IndustrialTrendChart),
                new PropertyMetadata("温度 (°C)", (d, e) => //d 是控件实例。e 是包含新值和旧值的包裹。
                {
                    var chart = (IndustrialTrendChart)d;
                    chart.MainPlot.Plot.Axes.Left.Label.Text = e.NewValue?.ToString();//MainPlot就是界面上的scottplot控件的名称
                    chart.MainPlot.Plot.Font.Automatic();
                    chart.MainPlot.Refresh();
                }));


        #endregion

        public IndustrialTrendChart()
        {
            InitializeComponent();
            LegendItemsControl.ItemsSource = _channelItems;
            Loaded += OnChartLoaded;
        }

        private void OnChartLoaded(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
            {
                InitPlotStyle();
                _isInitialized = true;
            }
            else
            {
                // 当从其他Tab页（如“历史试验”）切回当前页面时：
                // 如果图表中已有曲线数据，自动按现有数据自适应（保持0~100°C），绝不再强制重置为 (0, 180)
                if (_channelLoggers.Count > 0 && _channelLoggers.Values.Any(l => l.Data.Coordinates.Count > 0))
                {
                    ApplyIndustrialAutoScale();
                }
                MainPlot.Refresh();
            }
        }

        #region scottplot的初始化
        /// <summary>
        /// scottplot的初始化
        /// </summary>
        private void InitPlotStyle()
        {
            var plot = MainPlot.Plot;



            // 1. 设置坐标轴名称与字号
            plot.Axes.Left.Label.Text = YAxisLabel;
            plot.Axes.Left.Label.FontSize = 13;

            plot.Axes.Bottom.Label.Text = "试验持续时间";
            plot.Axes.Bottom.Label.FontSize = 13;



            // 2.🌟 先创建时间轴实例，再给这个新实例设置标题和样式：
            var dtAxis = plot.Axes.DateTimeTicksBottom();
            dtAxis.LabelText = "试验持续时间";
            dtAxis.LabelFontSize = 13;
            dtAxis.FrameLineStyle.Color = ScottPlot.Color.FromHex("#64748B");
            dtAxis.FrameLineStyle.Width = 1.5f;
            dtAxis.MinimumSize = 55;
            if (dtAxis.TickGenerator is ScottPlot.TickGenerators.DateTimeAutomatic dtGen)
            {
                dtGen.LabelFormatter = (dt) => dt.ToString("HH:mm:ss");
            }
            // 3.🌟 必须在所有轴的文字都赋值完毕后，调用字体自适应！
            plot.Font.Automatic();

            // 4.🌟 隐藏 ScottPlot 静态图例，改用右上角交互式 CheckBox 图例
            plot.HideLegend();

            // 🌟 创建十字准星（初始隐藏，鼠标滑入时跟随）
            _crosshair = plot.Add.Crosshair(0, 0);
            _crosshair.IsVisible = false;
            _crosshair.LineColor = ScottPlot.Color.FromHex("#94A3B8");
            _crosshair.LineWidth = 1.0f;
            _crosshair.LinePattern = ScottPlot.LinePattern.Dashed;

            // 5. 网格线与背景配色（画布改为纯白背景，导出 PNG 截图清晰干净）
            plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#E2E8F0");
            plot.Grid.MajorLineWidth = 1;
            plot.FigureBackground.Color = ScottPlot.Colors.White;
            plot.DataBackground.Color = ScottPlot.Color.FromHex("#FAFAFA");

            // 6. 初始视口范围：如果已经有曲线数据，直接自适应；只有全新无数据时才默认当前时间 30 分钟
            if (_channelLoggers.Count > 0 && _channelLoggers.Values.Any(l => l.Data.Coordinates.Count > 0))
            {
                ApplyIndustrialAutoScale();
            }
            else
            {
                DateTime now = DateTime.Now;
                plot.Axes.SetLimitsX(now.ToOADate(), now.AddMinutes(30).ToOADate());
                plot.Axes.SetLimitsY(0, 100);
            }


            // 🌟 7. 挂载全局渲染拦截器：无论鼠标怎么缩放、拖拽，在每帧渲染前锁定 Y 轴最低为 0
            if (!_isRenderHookRegistered)
            {
                plot.RenderManager.RenderStarting += (s, e) =>
                {
                    EnsureYAxesNonNegative();
                };
                _isRenderHookRegistered = true;
            }
            plot.Font.Automatic();

            // 1. 清空所有默认的鼠标按键行为
            MainPlot.UserInputProcessor.UserActionResponses.Clear();
            // 2. 只把“鼠标左键平移”加回来，不加任何滚轮缩放行为
            var leftButton = ScottPlot.Interactivity.StandardMouseButtons.Left;
            MainPlot.UserInputProcessor.UserActionResponses.Add(
                new ScottPlot.Interactivity.UserActionResponses.MouseDragPan(leftButton)
            );

            MainPlot.Refresh();
        }

        #endregion

        #region 获取或创建Y轴的方法
        /// <summary>
        /// 智能获取或创建对应的 Y 轴
        /// </summary>
        private ScottPlot.IYAxis GetOrCreateYAxis(string axisName)
        {
            // 如果已经建过这个轴（比如 "温度 (°C)"），直接返回复用
            if (_yAxes.TryGetValue(axisName, out var existingAxis))
            {
                return existingAxis;
            }

            ScottPlot.IYAxis newAxis;
            // 如果是第一条轴：直接使用默认的左侧主 Y 轴 (Axes.Left)
            if (_yAxes.Count == 0)
            {
                newAxis = MainPlot.Plot.Axes.Left;
                newAxis.Label.Text = axisName;
            }
            else
            {
                // 如果是第二条、第三条轴：自动在左侧并排追加新的 Y 轴！
                var leftAxis = MainPlot.Plot.Axes.AddLeftAxis();
                leftAxis.LabelText = axisName;
                leftAxis.LabelFontName = "Microsoft YaHei";
                leftAxis.LabelFontSize = 13;
                leftAxis.Min = 0; // 🌟 新增的左轴刻度绝不低于 0！
                newAxis = leftAxis;
            }

            _yAxes[axisName] = newAxis;
            return newAxis;
        }
        #endregion

        #region 智能色彩分配引擎 (前20色用专家色，超过20色用黄金分割出色)
        /// <summary>
        /// 动态获取下一个互不撞色的高对比度颜色
        /// </summary>
        private ScottPlot.Color GetNextDistinctColor(int index)
        {
            // 如果在前 20 个通道以内：直接拿 Category20 专家调色板！
            if (index < _palette20.Colors.Length)
            {
                return _palette20.GetColor(index);
            }
            // 超过 20 个通道：启用计算机图形学黄金分割角算法 (137.508°)
            // 保证色相在色轮上最大化分散，饱和度85%，亮度50%，鲜明可见！
            float goldenAngle = 137.508f;
            float hue = (float)((index * goldenAngle) % 360.0);
            return ScottPlot.Color.FromHSL(hue, 0.85f, 0.50f);
        }
        #endregion

        #region 对外提供的核心接口 (给主界面或ViewModel调用)
        /// <summary>
        /// 压入数据点（支持指定不同的 Y 轴）
        /// </summary>
        /// <param name="channelName">测点名称（如 "前轴承" 或 "实时转速"）</param>
        /// <param name="time">时间戳</param>
        /// <param name="value">数值</param>
        /// <param name="yAxisName">所属 Y 轴名称（如 "温度 (°C)" 或 "转速 (rpm)"，默认是当前控件的 YAxisLabel）</param>
        public void AddPoint(string channelName, DateTime time, double value, string? yAxisName = null)
        {
            string targetAxisName = string.IsNullOrEmpty(yAxisName) ? YAxisLabel : yAxisName;
            if (!_channelLoggers.TryGetValue(channelName, out var logger))
            {
                var color = GetNextDistinctColor(_channelCount);
                _channelCount++;
                logger = MainPlot.Plot.Add.DataLogger();
                logger.LegendText = channelName;
                logger.Color = color;
                logger.LineWidth = 2;

                // 禁用曲线自带的视野管理，禁止多条曲线互相打架！
                logger.ManageAxisLimits = false;

                logger.Axes.YAxis = GetOrCreateYAxis(targetAxisName);
                _channelLoggers[channelName] = logger;
                MainPlot.Plot.Font.Automatic();

                // 🌟 自动加入右上角交互式图例（默认勾选显示）
                var wpfBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));
                _channelItems.Add(new ChartChannelItem
                {
                    ChannelName = channelName,
                    ColorBrush = wpfBrush,
                    IsVisible = true
                });
            }

            // 压入数据点
            logger.Add(time.ToOADate(), value);

            //底刻度必须焊死在 0！只在温度突破上限时，向上扩容顶刻度
            MainPlot.Plot.Axes.Left.Min = 0;
            if (value > MainPlot.Plot.Axes.Left.Max - 5)
            {
                MainPlot.Plot.Axes.Left.Max = Math.Ceiling(value + 15);
            }

            // 改为提前 10 分钟预判延伸（消除撞墙压抑感）：
            // 10 分钟对应的 OADate 差值 = 10 / (24小时 * 60分钟)
            double twoMinutesOADate = 10.0 / (24.0 * 60.0);
            if (time.ToOADate() >= MainPlot.Plot.Axes.Bottom.Max - twoMinutesOADate)
            {
                MainPlot.Plot.Axes.Bottom.Max = time.AddMinutes(30).ToOADate();
            }

            MainPlot.Refresh();
        }

        /// <summary>
        /// 🌟 专业工业视野自适应：
        /// 1. Y 轴最低刻度恒定锁定在 0，绝不上浮！
        /// 2. 纵向包容所有已启用的高低温曲线，所有线一眼全收！
        /// 3. 横向包含试验从头到尾的完整时间！
        /// </summary>
        public void ApplyIndustrialAutoScale()
        {
            if (_channelLoggers.Count == 0) return;

            double maxTemp = 0;
            double minTime = double.MaxValue;
            double maxTime = double.MinValue;
            bool hasData = false;

            // 遍历所有通道的所有点位，算出真实的最高温度和起止时间
            foreach (var logger in _channelLoggers.Values)
            {
                if (logger.Data.Coordinates.Count == 0) continue;
                hasData = true;

                foreach (var pt in logger.Data.Coordinates)
                {
                    if (pt.Y > maxTemp) maxTemp = pt.Y;
                    if (pt.X < minTime) minTime = pt.X;
                    if (pt.X > maxTime) maxTime = pt.X;
                }
            }

            if (!hasData)
            {
                // 没数据时回到默认标准视野
                DateTime now = DateTime.Now;
                MainPlot.Plot.Axes.SetLimitsX(now.ToOADate(), now.AddMinutes(30).ToOADate());
                MainPlot.Plot.Axes.SetLimitsY(0, 100);
                return;
            }

            // 🌟 1. Y 轴：底刻度焊死在 0，顶刻度保证至少 100°C（若超过100则自适应再加20°C空间）
            double targetYMax = Math.Max(100, Math.Ceiling(maxTemp + 20));
            MainPlot.Plot.Axes.SetLimitsY(0, targetYMax);
            MainPlot.Plot.Axes.Left.Min = 0;

            // 所有附加的左侧多 Y 轴底刻度也锁定 0
            foreach (var axis in _yAxes.Values)
            {
                axis.Min = 0;
            }

            // 🌟 2. X 轴：包含试验开始到结束，若不足 30 分钟则保持 30 分钟全貌，避免视野过紧
            DateTime startTime = DateTime.FromOADate(minTime);
            DateTime endTime = DateTime.FromOADate(maxTime);
            DateTime targetXEnd = startTime.AddMinutes(30);

            if (endTime > targetXEnd.AddMinutes(-2))
            {
                targetXEnd = endTime.AddMinutes(5); // 试验跑久了之后向右留出 5 分钟余量
            }

            MainPlot.Plot.Axes.SetLimitsX(startTime.ToOADate(), targetXEnd.ToOADate());
        }
        /// <summary>
        /// 清空所有曲线重新开始
        /// </summary>
        public void Clear()
        {
            _channelLoggers.Clear();
            _channelCount = 0;
            _channelItems.Clear();
            if (HoverTooltipBorder != null) HoverTooltipBorder.Visibility = Visibility.Collapsed;
            MainPlot.Plot.Clear();
            InitPlotStyle();
        }
        #endregion

        #region 确保Y轴不会出现0刻度以下
        /// <summary>
        /// 确保所有左侧 Y 轴的最低刻度永远不小于 0（拒绝负数刻度）
        /// </summary>
        private void EnsureYAxesNonNegative()
        {
            // 1. 必须优先约束默认的主 Y 轴
            ClampAxis(MainPlot.Plot.Axes.Left);
            // 2. 约束所有额外动态添加的左侧多 Y 轴
            foreach (var axis in _yAxes.Values)
            {
                ClampAxis(axis);
            }
        }
        /// <summary>
        /// 单个坐标轴的防跌破 0 锁定逻辑
        /// </summary>
        private void ClampAxis(ScottPlot.IYAxis axis)
        {
            if (axis.Min < 0)
            {
                double span = Math.Max(10, axis.Max - axis.Min);
                axis.Min = 0;
                // 如果用户按住鼠标整体往下拖出了 0 以下，保证最大刻度不会小于等于0
                if (axis.Max <= 0)
                {
                    axis.Max = span;
                }
            }
        }
        #endregion

        #region 内部按钮事件处理
        private void BtnAutoScale_Click(object sender, RoutedEventArgs e)
        {
            // 🌟 调用工业级自适应（底刻度焊死为 0，6 条线全收！）
            ApplyIndustrialAutoScale();
            EnsureYAxesNonNegative();
            MainPlot.Refresh();
        }
        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            if (HandyControl.Controls.MessageBox.Show("确定要清空当前所有曲线记录吗？", "提示",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Clear();
            }
        }
        #region 曲线截图保存图片按钮及路径
        private void BtnSaveImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PNG 图片 (*.png)|*.png|JPEG 图片 (*.jpg)|*.jpg",
                FileName = $"温升曲线_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            };
            if (dlg.ShowDialog() == true)
            {
                MainPlot.Plot.SavePng(dlg.FileName, 1300, 800);
                HandyControl.Controls.Growl.Success("曲线截图已成功导出！");
            }
        }

        /// <summary>
        /// 供外部自动调用：将当前曲线渲染为高清图片并静默保存到指定路径
        /// </summary>
        public bool SaveImage(string filePath)
        {
            try
            {
                string? dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                // 保存 1300x800 分辨率的高清截图
                MainPlot.Plot.SavePng(filePath, 1300, 800);
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }
        #endregion



        #region 曲线的数据导出
        /// <summary>
        /// 实现曲线的数据导出为CSV文件（Excel型式）按钮执行的代码
        /// </summary>
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "CSV 数据表格 (*.csv)|*.csv",
                FileName = $"温升曲线_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (dlg.ShowDialog() == true)
            {
                if (SaveToCsv(dlg.FileName))
                    HandyControl.Controls.Growl.Success($"曲线数据已成功导出为 CSV！\n{dlg.FileName}");
                else
                    HandyControl.Controls.Growl.Warning("当前暂无有效曲线数据可导出！");
            }
        }

        public bool SaveToCsv(string filePath)
        {
            try
            {
                if (_channelLoggers.Count == 0 || !_channelLoggers.Values.Any(l => l.Data.Coordinates.Count > 0))
                    return false;
                string? dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var channelNames = _channelLoggers.Keys.ToList();

                //外层是 SortedDictionary<string, ...>：键（Key）是时间字符串（如 "14:00:00"）。用 SortedDictionary 的特点是无论插入顺序如何，它内部永远自动按时间从小到大升序排列！
                //内层是 Dictionary<string, double>：存这一秒下，哪个通道对应多少度（例如["前轴承"] = 28.5）。
                var timeRowMap = new SortedDictionary<string, Dictionary<string, double>>();

                foreach (var (channelName, logger) in _channelLoggers)
                {
                    foreach (var pt in logger.Data.Coordinates)
                    {
                        string timeStr = DateTime.FromOADate(pt.X).ToString("yyyy-MM-dd HH:mm:ss");
                        if (!timeRowMap.TryGetValue(timeStr, out var row))
                        {
                            row = new Dictionary<string, double>();
                            timeRowMap[timeStr] = row;
                        }
                        row[channelName] = pt.Y;
                    }
                }
                // 使用带 UTF-8 BOM 编码，防止 Excel 中文表头乱码
                using var writer = new StreamWriter(filePath, false, new UTF8Encoding(true));
                var headers = new List<string> { "采集时间" };
                headers.AddRange(channelNames);
                writer.WriteLine(string.Join(",", headers));
                foreach (var (timeStr, row) in timeRowMap)
                {
                    var lineCols = new List<string> { timeStr };
                    foreach (var ch in channelNames)
                    {
                        lineCols.Add(row.TryGetValue(ch, out double val) ? val.ToString("F2") : "");
                    }
                    writer.WriteLine(string.Join(",", lineCols));
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 仅供系统内部自动回显调用：从指定文件自动恢复重绘历史曲线
        /// </summary>
        public bool LoadFromCsv(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                var lines = File.ReadAllLines(filePath, Encoding.UTF8);
                if (lines.Length <= 1) return false;
                // 1. 清空当前图表
                Clear();
                // 2. 解析表头测点名称
                var headers = lines[0].Split(',');
                if (headers.Length < 2) return false;
                // 3. 逐行读入并压点重绘
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var cols = line.Split(',');
                    if (cols.Length < headers.Length) continue;
                    if (DateTime.TryParse(cols[0], out DateTime time))
                    {
                        for (int c = 1; c < headers.Length; c++)
                        {
                            string channelName = headers[c].Trim();
                            if (double.TryParse(cols[c], out double val))
                            {
                                AddPoint(channelName, time, val);
                            }
                        }
                    }
                }
                // 4. 重绘完毕后自动调整最佳视野
                ApplyIndustrialAutoScale();
                EnsureYAxesNonNegative();
                MainPlot.Refresh();
                return true;
            }
            catch
            {
                return false;
            }
        }
        #endregion


        #endregion


        #region 右上角图例 CheckBox 控制显隐
        private void OnChannelVisibilityChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is ChartChannelItem item)
            {
                if (_channelLoggers.TryGetValue(item.ChannelName, out var logger))
                {
                    logger.IsVisible = item.IsVisible;
                    MainPlot.Refresh();
                }
            }
        }
        #endregion

        #region 鼠标滑过显示十字准星与所有通道数值
        private void MainPlot_MouseMove(object sender, MouseEventArgs e)
        {
            if (_crosshair == null || _channelLoggers.Count == 0 || !_channelLoggers.Values.Any(l => l.Data.Coordinates.Count > 0))
            {
                HoverTooltipBorder.Visibility = Visibility.Collapsed;
                return;
            }

            var pos = e.GetPosition(MainPlot);
            var coords = MainPlot.Plot.GetCoordinates(new ScottPlot.Pixel(pos.X, pos.Y));

            // 更新十字准星位置
            _crosshair.Position = coords;
            _crosshair.IsVisible = true;

            // 转换 X 轴时间
            DateTime hoverDt = DateTime.FromOADate(coords.X);
            TxtTooltipTime.Text = $"⏱️ 时间: {hoverDt:HH:mm:ss}";

            // 遍历所有已勾选显示的通道，查找当前鼠标时间点的数值
            var valueList = new List<ChannelValueItem>();
            foreach (var chItem in _channelItems)
            {
                if (!chItem.IsVisible) continue; // 不勾选的不显示

                if (_channelLoggers.TryGetValue(chItem.ChannelName, out var logger) && logger.Data.Coordinates.Count > 0)
                {
                    double? val = GetNearestYValue(logger.Data.Coordinates, coords.X);
                    if (val.HasValue)
                    {
                        valueList.Add(new ChannelValueItem
                        {
                            ChannelName = chItem.ChannelName,
                            ColorBrush = chItem.ColorBrush,
                            ValueText = $"{val.Value:F1} ℃"
                        });
                    }
                }
            }

            TooltipItemsControl.ItemsSource = valueList;
            HoverTooltipBorder.Visibility = valueList.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            MainPlot.Refresh();
        }

        private void MainPlot_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_crosshair != null)
            {
                _crosshair.IsVisible = false;
                MainPlot.Refresh();
            }
            HoverTooltipBorder.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 二分查找最贴近指定 X 轴时间的数据点数值
        /// </summary>
        private double? GetNearestYValue(IReadOnlyList<ScottPlot.Coordinates> coords, double targetX)
        {
            if (coords == null || coords.Count == 0) return null;
            int low = 0, high = coords.Count - 1;
            while (low <= high)
            {
                int mid = (low + high) / 2;
                if (coords[mid].X < targetX) low = mid + 1;
                else high = mid - 1;
            }
            int best = Math.Clamp(low, 0, coords.Count - 1);
            if (high >= 0 && Math.Abs(coords[high].X - targetX) < Math.Abs(coords[best].X - targetX))
            {
                best = high;
            }
            return coords[best].Y;
        }
        #endregion
    }

    #region 辅助实体定义
    public class ChartChannelItem : INotifyPropertyChanged
    {
        public string ChannelName { get; set; } = string.Empty;
        public Brush ColorBrush { get; set; } = Brushes.SteelBlue;
        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class ChannelValueItem
    {
        public string ChannelName { get; set; } = string.Empty;
        public Brush ColorBrush { get; set; } = Brushes.SteelBlue;
        public string ValueText { get; set; } = string.Empty;
    }
    #endregion
}
