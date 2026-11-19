using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using thinger.MotorTypeTestSystem.Models;
using thinger.MotorTypeTestSystem.Models.NoLoadTestModels;

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

        // 3. 历史记录查询：支持按条码/型号模糊搜索，按测试时间倒序排列
        Task<List<MotorTestRecord>> QueryTestRecordsAsync(string? keyword = null);
        // 4. 删除某条测试记录（方便清理调试垃圾数据）
        Task<bool> DeleteTestRecordAsync(string id);

        #region 1. 保存/更新直流电阻数据 2. 根据主表流水号获取直流电阻数据（如果之前测过，一进页面就能自动回显数据）
        // 1. 保存/更新直流电阻数据
        Task<bool> SaveDcResistanceAsync(DcResistanceRecord record);
        // 2. 根据主表流水号获取直流电阻数据（如果之前测过，一进页面就能自动回显数据）
        Task<DcResistanceRecord?> GetDcResistanceByRecordIdAsync(string recordId);
        #endregion

        #region 1. 保存/更新温升试验数据  2. 根据主表流水号获取温升试验数据（进入页面自动回显）
        //  1. 保存/更新温升试验数据
        Task<bool> SaveTemperatureRiseAsync(TemperatureRiseRecord record);
        // 🌟 2. 根据主表流水号获取温升试验数据（进入页面自动回显）
        Task<TemperatureRiseRecord?> GetTemperatureRiseByRecordIdAsync(string recordId);
        #endregion

        #region 1. 保存/更新空载试验数据   2. 根据主表流水号获取空载试验数据（进入页面自动回显）
        //  1. 保存/更新空载试验数据
        Task<bool> SaveNoLoadTestRecordAsync(NoLoadTestRecord record);

        //2. 根据主表流水号获取空载试验数据（进入页面自动回显）
        Task<NoLoadTestRecord?> GetNoLoadTestRecordByRecordIdAsync(string recordId);
        #endregion


    }
}
