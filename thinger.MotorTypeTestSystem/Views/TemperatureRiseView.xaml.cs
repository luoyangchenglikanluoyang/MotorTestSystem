using System.Windows.Data;
using ScottPlot;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using thinger.MotorTypeTestSystem.Models;
using thinger.MotorTypeTestSystem.ViewModels;

namespace thinger.MotorTypeTestSystem.Views
{
    public partial class TemperatureRiseView : UserControl
    {
        public TemperatureRiseView()
        {
            InitializeComponent();
            InitCoolingPlotStyle();
            // 如果 Prism 此时已经注入好了 ViewModel，立刻绑定！
            if (DataContext is TemperatureRiseViewModel vm)
            {
                BindViewModel(vm);
            }

            // 监听后续可能的变化
            DataContextChanged += (s, e) =>
            {
                if (e.OldValue is TemperatureRiseViewModel oldVm)
                    UnbindViewModel(oldVm);

                if (e.NewValue is TemperatureRiseViewModel newVm)
                    BindViewModel(newVm);
            };

            // Loaded 时做一次安全兜底，万无一失
            Loaded += (s, e) =>
            {
                if (DataContext is TemperatureRiseViewModel loadedVm)
                    BindViewModel(loadedVm);
            };
        }

        private void BindViewModel(TemperatureRiseViewModel vm)
        {
            vm.RequestClearChart -= OnRequestClearChart;
            vm.RequestClearChart += OnRequestClearChart;

            vm.RequestAddPoint -= OnRequestAddPoint;
            vm.RequestAddPoint += OnRequestAddPoint;

            vm.RequestSaveChartData -= OnRequestSaveChartData;
            vm.RequestSaveChartData += OnRequestSaveChartData;

            vm.RequestLoadChartData -= OnRequestLoadChartData;
            vm.RequestLoadChartData += OnRequestLoadChartData;

            //绑定导出截图事件
            vm.RequestExportChartImage -= OnRequestExportChartImage;
            vm.RequestExportChartImage += OnRequestExportChartImage;

            // 挂载冷却外推画图事件
            vm.RequestDrawCoolingPlot -= OnRequestDrawCoolingPlot;
            vm.RequestDrawCoolingPlot += OnRequestDrawCoolingPlot;
            vm.RequestClearCoolingPlot -= OnRequestClearCoolingPlot;
            vm.RequestClearCoolingPlot += OnRequestClearCoolingPlot;

            // 监听测温通道配置变动事件，动态刷新工况表格列
            if (vm.TempChannelService != null)
            {
                vm.TempChannelService.ChannelsChanged -= OnChannelsChanged;
                vm.TempChannelService.ChannelsChanged += OnChannelsChanged;
            }
            RefreshDynamicTemperatureColumns();
        }


        private void UnbindViewModel(TemperatureRiseViewModel vm)
        {
            vm.RequestClearChart -= OnRequestClearChart;
            vm.RequestAddPoint -= OnRequestAddPoint;

            vm.RequestSaveChartData -= OnRequestSaveChartData;
            vm.RequestLoadChartData -= OnRequestLoadChartData;

            //注销导出截图事件
            vm.RequestExportChartImage -= OnRequestExportChartImage;

            // 注销冷却外推画图事件
            vm.RequestDrawCoolingPlot -= OnRequestDrawCoolingPlot;
            vm.RequestClearCoolingPlot -= OnRequestClearCoolingPlot;

            if (vm.TempChannelService != null)
            {
                vm.TempChannelService.ChannelsChanged -= OnChannelsChanged;
            }
        }

        private readonly System.Collections.Generic.List<DataGridColumn> _dynamicTempColumns = new();

        private void OnChannelsChanged()
        {
            Dispatcher.Invoke(RefreshDynamicTemperatureColumns);
        }

        /// <summary>
        /// 🌟 动态重构工况表格中的测温通道列 (测了什么通道就自动展示什么列，名称与配置保持同步)
        /// </summary>
        private void RefreshDynamicTemperatureColumns()
        {
            if (DataContext is not TemperatureRiseViewModel vm) return;
            if (ProcessDataGrid == null) return;

            // 1. 移除已存在的动态温度列
            foreach (var col in _dynamicTempColumns)
            {
                ProcessDataGrid.Columns.Remove(col);
            }
            _dynamicTempColumns.Clear();

            // 2. 找到操作列 ColOperation 的当前索引位置
            int insertIndex = ProcessDataGrid.Columns.IndexOf(ColOperation);
            if (insertIndex < 0) insertIndex = ProcessDataGrid.Columns.Count;

            var centerStyle = TryFindResource("CenterTextBlock") as Style;
            var redStyle = TryFindResource("HighlightRedTextBlock") as Style;

            // 3. 遍历全局配置的所有启用通道，动态生成并插入列
            if (vm.TempChannelService?.Channels != null)
            {
                foreach (var ch in vm.TempChannelService.Channels)
                {
                    if (!ch.IsEnabled) continue;

                    string headerText = string.IsNullOrWhiteSpace(ch.PointName)
                        ? $"{ch.ChannelDisplay} (℃)"
                        : $"{ch.PointName} (℃)";

                    var col = new DataGridTextColumn
                    {
                        Header = headerText,
                        Binding = new Binding($"[{ch.ChannelIndex}]") { StringFormat = "{0:F1}" },
                        Width = new DataGridLength(105),
                        ElementStyle = (ch.PointType == "绕组" && redStyle != null) ? redStyle : centerStyle
                    };

                    _dynamicTempColumns.Add(col);
                    ProcessDataGrid.Columns.Insert(insertIndex, col);
                    insertIndex++;
                }
            }
        }


        #region 倒推热态电阻曲线

        /// <summary>
        /// 🌟 初始化冷却外推图表的工业样式（底刻度定死在 0，清爽护眼浅色轴、彻底消除密集小短线）
        /// </summary>
        private void InitCoolingPlotStyle()
        {
            var plot = CoolingPlot.Plot;
            plot.Clear();

            // 1. 🌟 字体：采用微软专为屏幕优化的现代无衬线字体，告别发虚与厚重
            plot.Font.Set("Microsoft YaHei UI");

            // 2. 🌟 舒适护眼配色：告别死黑高对比度，采用现代工业浅石板灰
            var axisColor = ScottPlot.Color.FromHex("#94A3B8");      // 坐标轴边框线：柔和浅石板灰（不深、不扎眼）
            var labelColor = ScottPlot.Color.FromHex("#475569");     // 轴标题文字：典雅深灰
            var tickTextColor = ScottPlot.Color.FromHex("#64748B");  // 刻度数值：中性石板灰

            // X 轴配置
            plot.Axes.Bottom.FrameLineStyle.Color = axisColor;
            plot.Axes.Bottom.FrameLineStyle.Width = 1.2f;
            plot.Axes.Bottom.Label.Text = "停机流逝时间 t (秒)";
            plot.Axes.Bottom.Label.FontSize = 12.5f;
            plot.Axes.Bottom.Label.Bold = false;
            plot.Axes.Bottom.Label.ForeColor = labelColor;
            plot.Axes.Bottom.TickLabelStyle.FontSize = 11;
            plot.Axes.Bottom.TickLabelStyle.Bold = false;
            plot.Axes.Bottom.TickLabelStyle.ForeColor = tickTextColor;

            // Y 轴配置
            plot.Axes.Left.FrameLineStyle.Color = axisColor;
            plot.Axes.Left.FrameLineStyle.Width = 1.2f;
            plot.Axes.Left.Label.Text = "绕组线电阻 R (Ω)";
            plot.Axes.Left.Label.FontSize = 12.5f;
            plot.Axes.Left.Label.Bold = false;
            plot.Axes.Left.Label.ForeColor = labelColor;
            plot.Axes.Left.TickLabelStyle.FontSize = 11;
            plot.Axes.Left.TickLabelStyle.Bold = false;
            plot.Axes.Left.TickLabelStyle.ForeColor = tickTextColor;

            // 3. 🌟 彻底消灭密集的小横线（次刻度长度直接清零）
            plot.Axes.Bottom.MinorTickStyle.Length = 0;
            plot.Axes.Left.MinorTickStyle.Length = 0;

            // 主刻度短线精巧化（同轴线色，微短 3 像素）
            plot.Axes.Bottom.MajorTickStyle.Length = 3;
            plot.Axes.Bottom.MajorTickStyle.Color = axisColor;
            plot.Axes.Left.MajorTickStyle.Length = 3;
            plot.Axes.Left.MajorTickStyle.Color = axisColor;

            // 4. 🌟 稀疏大刻度密度：X 轴从原本 25 个数字拥挤精简为 6~7 个整齐大刻度（0, 20, 40, 60...）
            if (plot.Axes.Bottom.TickGenerator is ScottPlot.TickGenerators.NumericAutomatic xGen)
            {
                xGen.TargetTickCount = 7;
            }
            if (plot.Axes.Left.TickGenerator is ScottPlot.TickGenerators.NumericAutomatic yGen)
            {
                yGen.TargetTickCount = 6;
            }

            // 5. 背景网格线：超浅辅助线，纯净白底，视觉极度通透舒缓
            plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#F1F5F9");
            plot.Grid.MajorLineWidth = 1;
            plot.FigureBackground.Color = ScottPlot.Colors.White;
            plot.DataBackground.Color = ScottPlot.Colors.White;

            // 6. 初始视口范围（X 轴 0~120秒，Y 轴 0~2.0Ω），底刻度绝对定死在 0
            plot.Axes.SetLimitsX(0, 120);
            plot.Axes.SetLimitsY(0, 2.0);
            plot.Axes.Left.Min = 0;
            plot.Axes.Bottom.Min = 0;

            // 7. 彻底禁用鼠标交互
            CoolingPlot.UserInputProcessor.UserActionResponses.Clear();

            // 锁定防跌破 0 刻度
            plot.RenderManager.RenderStarting += (s, e) =>
            {
                if (plot.Axes.Left.Min < 0) plot.Axes.Left.Min = 0;
                if (plot.Axes.Bottom.Min < 0) plot.Axes.Bottom.Min = 0;
            };

            CoolingPlot.Refresh();
        }

        /// <summary>
        /// 🌟 在右侧 ScottPlot 画布上绘制实测散点与反向延长回归线
        /// </summary>
        private void OnRequestDrawCoolingPlot(List<CoolingPoint> points, double r0, double slope, int targetSec = 0, double targetR = 0)
        {
            if (points == null || points.Count == 0) return;
            var plot = CoolingPlot.Plot;
            plot.Clear();
            // 1. 应用统一的护眼柔和样式与字体
            plot.Font.Set("Microsoft YaHei UI");
            var axisColor = ScottPlot.Color.FromHex("#94A3B8");
            var labelColor = ScottPlot.Color.FromHex("#475569");
            var tickTextColor = ScottPlot.Color.FromHex("#64748B");
            plot.Axes.Bottom.FrameLineStyle.Color = axisColor;
            plot.Axes.Bottom.FrameLineStyle.Width = 1.2f;
            plot.Axes.Bottom.Label.Text = "停机流逝时间 t (秒)";
            plot.Axes.Bottom.Label.FontSize = 12.5f;
            plot.Axes.Bottom.Label.Bold = false;
            plot.Axes.Bottom.Label.ForeColor = labelColor;
            plot.Axes.Bottom.TickLabelStyle.FontSize = 11;
            plot.Axes.Bottom.TickLabelStyle.Bold = false;
            plot.Axes.Bottom.TickLabelStyle.ForeColor = tickTextColor;
            plot.Axes.Left.FrameLineStyle.Color = axisColor;
            plot.Axes.Left.FrameLineStyle.Width = 1.2f;
            plot.Axes.Left.Label.Text = "绕组线电阻 R (Ω)";
            plot.Axes.Left.Label.FontSize = 12.5f;
            plot.Axes.Left.Label.Bold = false;
            plot.Axes.Left.Label.ForeColor = labelColor;
            plot.Axes.Left.TickLabelStyle.FontSize = 11;
            plot.Axes.Left.TickLabelStyle.Bold = false;
            plot.Axes.Left.TickLabelStyle.ForeColor = tickTextColor;
            // 彻底消灭密集小横线
            plot.Axes.Bottom.MinorTickStyle.Length = 0;
            plot.Axes.Left.MinorTickStyle.Length = 0;
            plot.Axes.Bottom.MajorTickStyle.Length = 3;
            plot.Axes.Bottom.MajorTickStyle.Color = axisColor;
            plot.Axes.Left.MajorTickStyle.Length = 3;
            plot.Axes.Left.MajorTickStyle.Color = axisColor;
            if (plot.Axes.Bottom.TickGenerator is ScottPlot.TickGenerators.NumericAutomatic xGen)
            {
                xGen.TargetTickCount = 20;
            }
            if (plot.Axes.Left.TickGenerator is ScottPlot.TickGenerators.NumericAutomatic yGen)
            {
                yGen.TargetTickCount = 6;
            }
            plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#F1F5F9");
            plot.Grid.MajorLineWidth = 1;
            plot.FigureBackground.Color = ScottPlot.Colors.White;
            plot.DataBackground.Color = ScottPlot.Colors.White;
            // 2. 绘制实测采样点（精巧现代科技蓝，大小 7.5 不笨拙）
            double[] xs = points.Select(p => p.Seconds).ToArray();
            double[] ys = points.Select(p => p.Resistance).ToArray();
            var scatter = plot.Add.Scatter(xs, ys);
            scatter.LegendText = "现场实测点";
            scatter.Color = ScottPlot.Color.FromHex("#3B82F6");
            scatter.MarkerSize = 7.5f;
            scatter.LineWidth = 0; // 只画散点，不画折线
            // 3. 绘制外推回归直线：线宽细化为细腻的 2.0f，活力橙
            double maxX = xs.Max() + 15;
            double[] lineX = new double[] { 0, maxX };
            double[] lineY = new double[] { r0, r0 + slope * maxX };
            var regLine = plot.Add.ScatterLine(lineX, lineY);
            regLine.LegendText = $"外推回归线 (R0={r0:F5}Ω)";
            regLine.Color = ScottPlot.Color.FromHex("#F97316");
            regLine.LineWidth = 2.0f;
            // 4. 在 t=0 纵轴上绘制截距 R0（精致圆点 + 清爽不粗重文字）
            var star = plot.Add.Marker(0, r0);
            star.Shape = ScottPlot.MarkerShape.FilledCircle;
            star.Size = 8.5f;
            star.Color = ScottPlot.Color.FromHex("#EF4444");
            var txt = plot.Add.Text($"★ R0={r0:F5}Ω", 1.5, r0);
            txt.LabelFontSize = 12;
            txt.LabelFontColor = ScottPlot.Color.FromHex("#EF4444");
            txt.LabelBold = false; // 取消生硬加粗，字迹平滑清爽
            // 5. 坐标轴范围
            double targetYMax = Math.Max(1.0, Math.Ceiling(r0 * 1.3 * 10) / 10.0);
            plot.Axes.SetLimitsX(0, maxX);
            plot.Axes.SetLimitsY(0, targetYMax);
            plot.Axes.Left.Min = 0;
            plot.Axes.Bottom.Min = 0;
            // 6. 图例精美化
            plot.ShowLegend(Alignment.UpperRight);
            plot.Legend.FontSize = 11;
            plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#F8FAFC");
            plot.Legend.OutlineColor = ScottPlot.Color.FromHex("#E2E8F0");
            plot.Legend.OutlineWidth = 1;
            CoolingPlot.Refresh();
        }

        /// <summary>
        /// 清空冷却外推图表（清空后回到标准默认视野）
        /// </summary>
        private void OnRequestClearCoolingPlot()
        {
            InitCoolingPlotStyle();
        }
        #endregion

        private bool OnRequestSaveChartData(string filePath) => TrendChart.SaveToCsv(filePath);
        private bool OnRequestLoadChartData(string filePath) => TrendChart.LoadFromCsv(filePath);

        private bool OnRequestExportChartImage(string filePath) => TrendChart.SaveImage(filePath);

        /// <summary>
        /// 调用用户控件IndustrialTrendChart里的清理曲线的方法
        /// </summary>
        private void OnRequestClearChart() => TrendChart.Clear();

        /// <summary>
        /// 调用用户控件IndustrialTrendChart里的绘制曲线的方法
        /// </summary>
        /// <param name="channelName"></param>
        /// <param name="time"></param>
        /// <param name="value"></param>
        private void OnRequestAddPoint(string channelName, DateTime time, double value)
        {
            TrendChart.AddPoint(channelName, time, value);
        }

        /// <summary>
        /// 解决 WPF DataGrid 无法取消选中的问题：
        /// 1. 再次点击已选中的行 -> 自动取消选中！
        /// 2. 点击表格空白处 -> 自动取消选中！
        /// </summary>
        private void ProcessDataGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid dg)
            {
                var dep = e.OriginalSource as DependencyObject;
                // 如果点在按钮上（例如删除按钮），不干扰按钮的正常点击命令
                if (FindVisualParent<Button>(dep) != null) return;

                var row = FindVisualParent<DataGridRow>(dep);
                if (row == null)
                {
                    // 点在表格空白区域，清空当前选中
                    dg.SelectedItem = null;
                }
                else if (row.IsSelected)
                {
                    // 🌟 核心：若点在已经高亮选中的那一行，再次点击直接取消选择！
                    row.IsSelected = false;
                    dg.SelectedItem = null;
                    e.Handled = true;
                }
            }
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent) return parent;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }
    }
}
