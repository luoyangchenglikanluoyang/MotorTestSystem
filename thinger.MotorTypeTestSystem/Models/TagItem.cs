using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace thinger.MotorTypeTestSystem.Models
{
    /// <summary>
    /// 这个类代表全局变量池里的“一个数据”。它必须能通知界面更新。
    /// </summary>
    public partial class TagItem:ObservableObject
    {
        // 变量的名字（比如 "IO.Voltage_U"）
        public string TagName { get; } = string.Empty;

        [ObservableProperty]
        private object _value;

        // 构造函数
        public TagItem(string name, object initialValue = null)
        {
            TagName = name;
            Value = initialValue;
        }
    }
}
