using System;
using System.Collections.ObjectModel;
using thinger.MotorTypeTestSystem.Models;

namespace thinger.MotorTypeTestSystem.Services
{
    /// <summary>
    /// 全局测温通道配置服务接口
    /// 单例注入，保证父界面与子试验界面使用同一份通道配置
    /// </summary>
    public interface ITempChannelService
    {
        /// <summary>
        /// 全局测温通道列表（8路）
        /// </summary>
        ObservableCollection<TempChannelConfig> Channels { get; }

        /// <summary>
        /// 从配置文件加载通道配置
        /// </summary>
        void LoadConfig();

        /// <summary>
        /// 保存通道配置到本地文件
        /// </summary>
        void SaveConfig();

        /// <summary>
        /// 当通道配置被更新保存时触发
        /// </summary>
        event Action? ChannelsChanged;
    }
}
