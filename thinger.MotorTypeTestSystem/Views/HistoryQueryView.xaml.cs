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
using thinger.MotorTypeTestSystem.Models;
using thinger.MotorTypeTestSystem.ViewModels;

namespace thinger.MotorTypeTestSystem.Views
{
    /// <summary>
    /// HistoryQueryView.xaml 的交互逻辑
    /// </summary>
    public partial class HistoryQueryView : UserControl
    {
        public HistoryQueryView()
        {
            InitializeComponent();
        }
        /// <summary>
        /// 双击某一行数据时，自动触发进入该电机的试验现场
        /// </summary>
        private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is HistoryQueryViewModel vm && sender is DataGridRow row && row.Item is MotorTestRecord record)
            {
                vm.OpenExecutionCommand.Execute(record);
            }
        }
    }
}
