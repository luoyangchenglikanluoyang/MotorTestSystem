using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using thinger.MotorTypeTestSystem.Models;

namespace thinger.MotorTypeTestSystem.Services
{
    /// <summary>
    /// 电机测试记录管理接口
    /// </summary>
    public interface IMotorTestRepository
    {
        // 1. 插入一条电机试验新记录
        Task<bool> AddTestRecordAsync(MotorTestRecord record);

        // 2.更改电机名牌信息保存到数据库
        Task<bool> UpdateTestRecordAsync(MotorTestRecord record);


        // 1. 保存/更新直流电阻数据
        Task<bool> SaveDcResistanceAsync(DcResistanceRecord record);
        // 2. 根据主表流水号获取直流电阻数据（如果之前测过，一进页面就能自动回显数据）
        Task<DcResistanceRecord?> GetDcResistanceByRecordIdAsync(string recordId);
    }
}
