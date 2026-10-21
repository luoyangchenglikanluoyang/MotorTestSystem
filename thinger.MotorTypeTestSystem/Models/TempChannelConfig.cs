using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace thinger.MotorTypeTestSystem.Models
{
    /// <summary>
    /// 温升试验的通道配置类
    /// </summary>
    public partial class TempChannelConfig : ObservableObject
    {
        /// <summary>
        /// 物理通道编号 (如 1, 2, 3...)
        /// </summary>
        public int ChannelIndex { get; set; }

        /// <summary>
        /// 显示的物理端子名称（给人看的，固定不变）
        /// </summary>
        public string ChannelDisplay => $"通道 {ChannelIndex}";

        /// <summary>
        /// 对应 tags.json 里的 TagName (如 Temp.CH1)
        /// 后台去 TagPoolService 取数就凭它，固定不变！
        /// </summary>
        public string TagName { get; set; } = string.Empty;

        /// <summary>
        /// 用户自己起的名字（前台直接打字改，如：前轴承、机壳表面）
        /// </summary>
        [ObservableProperty]
        private string _pointName = string.Empty;

        /// <summary>
        /// 用户是否启用这个通道
        /// </summary>
        [ObservableProperty]
        private bool _isEnabled = true;

        /// <summary>
        /// 测点类型：如 "环境基准"、"机壳"、"轴承"、"绕组PT100"
        /// </summary>
        [ObservableProperty]
        private string _pointType = "常规测点";

        /// <summary>
        /// 是否作为计算温升的环境基准温度
        /// </summary>
        [ObservableProperty]
        private bool _isAmbient = false;
    }
}
