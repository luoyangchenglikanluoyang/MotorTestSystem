using ScottPlot;
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
using thinger.MotorTypeTestSystem.ViewModels;

namespace thinger.MotorTypeTestSystem.Views
{
    /// <summary>
    /// NoLoadTestView.xaml 的交互逻辑
    /// </summary>
    public partial class NoLoadTestView : UserControl
    {
        private NoLoadTestViewModel? _vm;
        // 保存左侧 4 根 Y 轴的引用（从右往左排：P0 最靠画布，I0 在最外侧）
        private readonly IYAxis? _axisP0;
        private readonly IYAxis? _axisPfe;
        private readonly IYAxis? _axisPc;
        private readonly IYAxis? _axisI0;
        public NoLoadTestView()
        {
            InitializeComponent();
            
            if (DataContext is NoLoadTestViewModel initialVm)
            {
                BindViewModel(initialVm);
            }

            this.DataContextChanged += OnDataContextChanged;

            // 加载时安全兜底Loaded事件是所有控件都准备好了后，即将要显示在屏幕上的时候执行的方法事件
            this.Loaded += (s, e) =>
            {
                if (_vm == null && DataContext is NoLoadTestViewModel loadedVm)
                {
                    BindViewModel(loadedVm);
                }
            };

            // 禁用鼠标滚轮/拖拽交互，防止在 ScrollViewer 滚动页面时把曲线缩歪
            NoLoadPlot.UserInputProcessor.IsEnabled = false;
            var plot = NoLoadPlot.Plot;
            // 2. 一次性建好左侧 4 根 Y 轴并配色
            _axisP0 = InitLeftAxis(plot.Axes.Left, "P₀ (W)", "#2563EB"); // 蓝
            _axisPfe = InitLeftAxis(plot.Axes.AddLeftAxis(), "Pfe (W)", "#059669"); // 绿
            _axisPc = InitLeftAxis(plot.Axes.AddLeftAxis(), "Pc (W)", "#7C3AED"); // 紫
            _axisI0 = InitLeftAxis(plot.Axes.AddLeftAxis(), "I₀ (A)", "#EA580C"); // 橙

            // 3. 设底轴标题和基础背景
            plot.Axes.Bottom.Label.Text = "(U₀/Uₙ)   /   (U₀/Uₙ)²";
            plot.Axes.Bottom.Label.FontSize = 12;
            plot.Axes.Bottom.MajorTickStyle.Length = 3;
            plot.Axes.Bottom.MinorTickStyle.Length = 0; // 彻底消灭底轴小刻度
            if (plot.Axes.Bottom.TickGenerator is ScottPlot.TickGenerators.NumericAutomatic xGen)
            {
                xGen.TargetTickCount = 10; // X轴大刻度精简为约7个整齐大刻度，告别拥挤
            }

            plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#E2E8F0");//把图表内部的辅助网格线，颜色设为极浅的灰蓝色（#E2E8F0）。
            plot.FigureBackground.Color = ScottPlot.Colors.White;// 外层背景曲线坐标轴以外
            plot.DataBackground.Color = ScottPlot.Color.FromHex("#FAFAFA");// 内层背景曲线坐标轴以内

            // 4. 隐藏右侧与顶部空白轴的刻度，让边框干净
            plot.Axes.Right.TickLabelStyle.IsVisible = false;
            plot.Axes.Right.MajorTickStyle.Length = 0;
            plot.Axes.Right.MinorTickStyle.Length = 0;
            plot.Axes.Top.TickLabelStyle.IsVisible = false;
            plot.Axes.Top.MajorTickStyle.Length = 0;
            plot.Axes.Top.MinorTickStyle.Length = 0;

            ClearPlots(); // 刚打开页面先显示空白坐标系
        }
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is NoLoadTestViewModel oldVm)
            {
                UnbindViewModel(oldVm);
            }

            if (e.NewValue is NoLoadTestViewModel newVm)
            {
                BindViewModel(newVm);
            }
        }

        private void BindViewModel(NoLoadTestViewModel vm)
        {
            _vm = vm;
            _vm.RequestDrawPlots -= DrawPlots;
            _vm.RequestDrawPlots += DrawPlots;
            _vm.RequestClearPlots -= ClearPlots;
            _vm.RequestClearPlots += ClearPlots;
            _vm.RequestExportChartImage -= ExportPng;
            _vm.RequestExportChartImage += ExportPng;
        }

        private void UnbindViewModel(NoLoadTestViewModel vm)
        {
            vm.RequestDrawPlots -= DrawPlots;
            vm.RequestClearPlots -= ClearPlots;
            vm.RequestExportChartImage -= ExportPng;
            if (_vm == vm) _vm = null;
        }

        /// <summary>
        /// 绘制曲线
        /// </summary>
        private void DrawPlots()
        {
            if (_vm == null || _vm.NoLoadTestPoints.Count == 0)
            {
                ClearPlots();
                return;
            }
            Dispatcher.Invoke(() =>
            {
                var plot = NoLoadPlot.Plot;
                //只清空曲线和文字，保留 4 根左侧Y轴
                plot.Clear();

                // 准备数组（表格是降序的，画图按电压标幺值升序排）
                var pts = _vm.NoLoadTestPoints.OrderBy(p => p.VoltageRatio).ToList();
                double[] uRatio = pts.Select(p => p.VoltageRatio).ToArray();
                double[] p0 = pts.Select(p => p.InputPower).ToArray();
                double[] pfe = pts.Select(p => p.IronLoss).ToArray();
                double[] i0 = pts.Select(p => p.Current).ToArray();

                // ① 空载功率 P0 = f(U0/UN)
                AddLine(uRatio, p0, _axisP0, "P₀ = f(U₀/Uₙ)", "#2563EB", smooth: true);

                // ② 铁耗 Pfe = f(U0/UN)（核算后才有值）
                bool hasPfe = pfe.Any(v => v > 0);//判断一下是否有真实有效的铁耗值
                if (hasPfe)AddLine(uRatio, pfe, _axisPfe, "Pfe = f(U₀/Uₙ)", "#059669", smooth: true);

                // ③ 低压段净损耗 Pc（恒定损耗） = f((U0/UN)²) + 虚线外推至截距 Pfw
                var lowPts = pts.Where(p => p.VoltageRatio <= 0.65).ToList();
                double maxPc = 0;
                if (lowPts.Count >= 2)
                {
                    double[] u2 = lowPts.Select(p => p.VoltageRatio * p.VoltageRatio).ToArray();
                    double[] pc = lowPts.Select(p => p.NetLoss).ToArray();
                    maxPc = pc.Max();
                    AddLine(u2, pc, _axisPc, "Pc = f(U₀/Uₙ)²", "#7C3AED", smooth: false);
                    double pfw = _vm.NoLoadRecord.UncorrectedMechanicalLoss > 0
                                ? _vm.NoLoadRecord.UncorrectedMechanicalLoss
                                : _vm.NoLoadRecord.MechanicalLoss;
                    if (pfw > 0)
                    {
                        maxPc = Math.Max(maxPc, pfw);
                        double maxU2 = u2.Max(); // 贯穿到低压段最大点
                        //红色外推虚线从截距 (0, Pfw) 贯穿延伸到低压段最大实测点
                        var ext = plot.Add.Scatter(new[] { 0.0, maxU2 }, new[] { pfw, pc.Last() });
                        ext.Color = ScottPlot.Color.FromHex("#DC2626");
                        ext.LineWidth = 2.0f;
                        ext.LinePattern = LinePattern.Dashed;
                        ext.MarkerSize = 0;
                        ext.Axes.YAxis = _axisPc;
                        // 标出截距红点
                        var dot = plot.Add.Marker(0, pfw);
                        dot.Color = ScottPlot.Color.FromHex("#DC2626");
                        dot.Size = 8;
                        dot.Axes.YAxis = _axisPc;
                        // 截距数值标注
                        var txt = plot.Add.Text($" Pfw={pfw:F1}W", 0.02, pfw);
                        txt.LabelStyle.ForeColor = ScottPlot.Color.FromHex("#DC2626");
                        txt.LabelStyle.FontSize = 11;
                        txt.LabelStyle.Bold = true;
                        txt.Axes.YAxis = _axisPc;
                    }
                }
                // ④ 空载电流 I0 = f(U0/UN)
                AddLine(uRatio, i0, _axisI0, "I₀ = f(U₀/Uₙ)", "#EA580C", smooth: true);

                // 额定电压基准虚线（X = 1.0）
                var vLine = plot.Add.VerticalLine(1.0);
                vLine.Color = ScottPlot.Color.FromHex("#94A3B8");
                vLine.LineWidth = 1.5f;
                vLine.LinePattern = LinePattern.Dashed;
                vLine.Text = "U₀/Uₙ = 1";

                //分别锁死 X 轴和 4 根 Y 轴的量程（通过系数把 4 条线错开，不糊在一起）
                double maxX = Math.Max(1.2, uRatio.Max() * 1.08);
                double maxP0 = Math.Max(10, p0.Max());
                double maxPfe = hasPfe ? Math.Max(10, pfe.Max()) : maxP0;
                double maxI0 = Math.Max(1, i0.Max());
                if (maxPc <= 0) maxPc = maxP0;

                plot.Axes.SetLimitsX(0, maxX);
                plot.Axes.SetLimitsY(0, maxP0 * 1.15, _axisP0);   // 最高
                plot.Axes.SetLimitsY(0, maxPfe * 1.65, _axisPfe);  // 居中
                plot.Axes.SetLimitsY(0, maxPc * 2.20, _axisPc);   // 偏下（看左下截距）
                plot.Axes.SetLimitsY(0, maxI0 * 2.40, _axisI0);   // 最下

                //显示图例 + 统一设中文字体（防止方块乱码）+ 刷屏
                plot.ShowLegend(Alignment.UpperLeft);
                plot.Font.Set("Microsoft YaHei");
                NoLoadPlot.Refresh();
            });
        }

        /// <summary>
        /// 往指定 Y 轴加一条折线/平滑曲线
        /// </summary>
        private void AddLine(double[] xs, double[] ys, IYAxis yAxis, string legend, string colorHex, bool smooth)
        {
            var sp = NoLoadPlot.Plot.Add.Scatter(xs, ys);
            sp.LegendText = legend;//设置图例文字。比如传进来 "铁耗 Pfe"，图表的右上角或左下角就会显示这个名字，并配上对应的颜色小方块。
            sp.Color = ScottPlot.Color.FromHex(colorHex);//把刚刚解析好的十六进制色值（比如红色、青色），应用到这条线上，线上的点和线本身都会变成这个颜色。
            sp.LineWidth = 2.2f;//设置线条的粗细为 2.2 像素。
            sp.MarkerSize = 6;//设置数据点（圆点/方块）的大小。如果你把数据点画成了一个个小圆圈，6 的大小在屏幕上看起来非常精致。如果只想看线不想看圆点，这里通常需要配合隐藏 MarkShape 的属性（虽然你这行没隐
            sp.Smooth = smooth;//ScottPlot 会用贝塞尔曲线把点连起来，看起来像丝滑的水波。空载特性曲线通常需要设为 true，因为电机的损耗变化是连续平滑的，折线会显得像数据有跳变。
            sp.Axes.YAxis = yAxis; // 关键：指定挂在哪一根左轴上
        }

        /// <summary>
        /// 统一设置每根左轴的颜色、标题和紧凑字号
        /// </summary>
        private static IYAxis InitLeftAxis(IYAxis axis, string title, string colorHex)
        {
            if (axis is ScottPlot.AxisPanels.YAxisBase yBase)
            {
                yBase.MinimumSize = 40; // 为每根左轴留出 40px 的安全间距防止重叠
            }
            var c = ScottPlot.Color.FromHex(colorHex);//把传进来的十六进制颜色字符串（比如 "#4ADE80"），转换成 ScottPlot 库自己能识别的颜色对象。
            axis.Label.Text = title;
            axis.Label.ForeColor = c;
            axis.Label.FontSize = 11;
            axis.Label.Bold = true;

            axis.TickLabelStyle.ForeColor = c;// 把轴上数字（比如 0, 10, 20）的颜色，也涂成颜色 c
            axis.TickLabelStyle.FontSize = 10;// 数字字号设为 10 号（比标题稍微小一点，主次分明）

            axis.FrameLineStyle.Color = c;// 坐标轴那根粗壮的边框主线，颜色涂成 c
            axis.MajorTickStyle.Color = c;// 主刻度线（长的刻度线），颜色涂成 c
            axis.MajorTickStyle.Length = 3;// 主刻度微短 3 像素，精巧利落
            axis.MinorTickStyle.Length = 0;// 彻底消灭密集的小横线（次刻度长度清零，告别梳齿感）

            // 稀疏大刻度密度：Y 轴从原本挤成一团的 20 多个数字精简为 6 个左右整齐大刻度（如 0, 20, 40, 60...）
            if (axis.TickGenerator is ScottPlot.TickGenerators.NumericAutomatic yGen)
            {
                yGen.TargetTickCount = 6;
            }
            return axis;
        }

        /// <summary>
        /// 清空曲线
        /// </summary>
        private void ClearPlots()
        {
            Dispatcher.Invoke(() =>
            {
                var plot = NoLoadPlot.Plot;
                plot.Clear();
                plot.Axes.SetLimitsX(0, 1.5);
                plot.Axes.SetLimitsY(0, 100, _axisP0);
                plot.Axes.SetLimitsY(0, 100, _axisPfe);
                plot.Axes.SetLimitsY(0, 100, _axisPc);
                plot.Axes.SetLimitsY(0, 10, _axisI0);
                plot.Font.Set("Microsoft YaHei");
                NoLoadPlot.Refresh();
            });
        }

        /// <summary>
        /// 导出曲线截图
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        private bool ExportPng(string filePath)
        {
            try
            {
                NoLoadPlot.Plot.SavePng(filePath, 1150, 650);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
