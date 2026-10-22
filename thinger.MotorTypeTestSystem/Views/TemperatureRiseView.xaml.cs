using System;
using System.Windows;
using System.Windows.Controls;
using thinger.MotorTypeTestSystem.ViewModels;

namespace thinger.MotorTypeTestSystem.Views
{
    public partial class TemperatureRiseView : UserControl
    {
        public TemperatureRiseView()
        {
            InitializeComponent();

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
        }

        private void UnbindViewModel(TemperatureRiseViewModel vm)
        {
            vm.RequestClearChart -= OnRequestClearChart;
            vm.RequestAddPoint -= OnRequestAddPoint;
        }

        private void OnRequestClearChart() => TrendChart.Clear();

        private void OnRequestAddPoint(string channelName, DateTime time, double value)
        {
            TrendChart.AddPoint(channelName, time, value);
        }
    }
}