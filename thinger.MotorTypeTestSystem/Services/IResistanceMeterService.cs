using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace thinger.MotorTypeTestSystem.Services
{
    /// <summary>
    /// 直流电阻测试仪（微欧计/电桥）统一采集接口
    /// 提供冷态电阻与温升热态电阻的通用读取能力
    /// </summary>
    public interface IResistanceMeterService
    {
        /// <summary>
        /// 单次异步读取微欧计当前实测电阻值（单位：Ω）
        /// </summary>
        /// <returns>(是否成功, 读取到的实测阻值Ω, 提示或错误消息)</returns>
        Task<(bool Success, double Resistance, string Message)> ReadResistanceAsync();
    }
}
