using Microsoft.Win32;
using ScottPlot;
using ScottPlot.Plottables;
using System;
using System.Collections.Generic;
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

namespace thinger.MotorTypeTestSystem.Controls
{
    /// <summary>
    /// IndustrialTrendChart.xaml 的交互逻辑
    /// </summary>
    public partial class IndustrialTrendChart : UserControl
    {
        // 1. 存储已创建的通道曲线字典 (通道名称 -> 曲线DataLogger)
        private readonly Dictionary<string, DataLogger> _channelLoggers = new();

        // 2. 引入 ScottPlot 官方权威的 20 色调色板
        private readonly ScottPlot.Palettes.Category20 _palette20 = new();

        // 记录当前已经分发了多少个通道颜色
        private int _channelCount = 0;

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
                    chart.MainPlot.Refresh();
                }));


        #endregion

        public IndustrialTrendChart()
        {
            InitializeComponent();
            Loaded += (s, e) => InitPlotStyle();
        }

        private void InitPlotStyle()
        {
            var plot = MainPlot.Plot;

            // 1. 设置坐标轴与字体
            plot.Axes.Left.Label.Text = YAxisLabel;
            plot.Axes.Left.Label.FontName = "Microsoft YaHei";
            plot.Axes.Left.Label.FontSize = 13;

            plot.Axes.Bottom.Label.Text = "试验持续时间";
            plot.Axes.Bottom.Label.FontName = "Microsoft YaHei";
            plot.Axes.Bottom.Label.FontSize = 13;

            // 2. 将 X 轴设置为时间格式
            plot.Axes.DateTimeTicksBottom();

            // 3. 打开内置图例 (右上角，美化背景)
            plot.ShowLegend(Alignment.UpperRight);
            plot.Legend.FontName = "Microsoft YaHei";
            plot.Legend.FontSize = 12;

            // 4. 网格线淡灰色
            plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#E2E8F0");
            plot.Grid.MajorLineWidth = 1;
            plot.FigureBackground.Color = ScottPlot.Colors.Transparent;
            plot.DataBackground.Color = ScottPlot.Color.FromHex("#FAFAFA");
            // 5. 初始 Y 轴默认范围 (0 ~ 120)
            plot.Axes.SetLimitsY(0, 120);

            MainPlot.Refresh();
        }

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
        /// 核心方法：向指定通道压入一个实时数据点
        /// </summary>
        public void AddPoint(string channelName, DateTime time, double value)
        {
            // 如果这条曲线还没创建过，自动分配颜色并新建曲线
            if (!_channelLoggers.TryGetValue(channelName, out var logger))
            {
                // 智能获取高对比度色彩
                var color = GetNextDistinctColor(_channelCount);
                _channelCount++;
                logger = MainPlot.Plot.Add.DataLogger();
                logger.LegendText = channelName;
                logger.Color = color;
                logger.LineWidth = 2;
                _channelLoggers[channelName] = logger;
            }
            // 压入时间戳与数值
            logger.Add(time.ToOADate(), value);
            // 视野自适应拓展并重绘
            MainPlot.Plot.Axes.AutoScale();
            MainPlot.Refresh();
        }
        /// <summary>
        /// 清空所有曲线重新开始
        /// </summary>
        public void Clear()
        {
            _channelLoggers.Clear();
            _channelCount = 0;
            MainPlot.Plot.Clear();
            InitPlotStyle();
        }
        #endregion

        #region 内部按钮事件处理
        private void BtnAutoScale_Click(object sender, RoutedEventArgs e)
        {
            MainPlot.Plot.Axes.AutoScale();
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
        private void BtnSaveImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PNG 图片 (*.png)|*.png|JPEG 图片 (*.jpg)|*.jpg",
                FileName = $"温升曲线_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            };
            if (dlg.ShowDialog() == true)
            {
                MainPlot.Plot.SavePng(dlg.FileName, 1200, 700);
                HandyControl.Controls.Growl.Success("曲线截图已成功导出！");
            }
        }
        #endregion

    }
}
