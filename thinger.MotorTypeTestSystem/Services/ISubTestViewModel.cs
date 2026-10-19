using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using thinger.MotorTypeTestSystem.Models;

namespace thinger.MotorTypeTestSystem.Services
{
    /// <summary>
    /// 所有子试验 ViewModel 的统一接口：用于接收主表传过来的电机档案
    /// </summary>
    public interface ISubTestViewModel
    {
        /// <summary>
        /// 初始化子试验，传入主表的电机建档记录（内含主表主键 Id 及额定参数）
        /// </summary>
        void InitializeTestRecord(MotorTestRecord mainRecord);
    }
}
