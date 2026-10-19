using HandyControl.Controls;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Text;
using System.Threading.Tasks;
using thinger.MotorTypeTestSystem.Models;
using Thinger.DatabaseOperationlibrary;

namespace thinger.MotorTypeTestSystem.Services
{
    public class MotorTestRepository : IMotorTestRepository
    {
        private readonly IDatabaseService _dbService;
        private readonly ILogger _Logger;

        public MotorTestRepository(IDatabaseService databaseService,ILogger logger)
        {
            _dbService = databaseService;
            _Logger = logger;
        }
        public async Task<bool> AddTestRecordAsync(MotorTestRecord Testrecord)
        {
            try
            {
                // ====================写入数据库 ====================
                string sql = @"INSERT INTO motor_test_record 
                    (Id, MotorModel, MotorSerialNumber, TestTime, ProductionTime,
                     RatedPower, RatedVoltage, RatedCurrent, RatedFrequency,
                     RatedTorque, RatedSpeed, NominalEfficiency, RatedPowerFactor,
                     RotationalInertia, Series, WorkSystem, ConnectionMethod,
                     InsulationClass, ProtectionLevel, TemperatureRiseClass,
                     K1, K2, SelectedStandard, TestResultStatus)
                    VALUES 
                    (@Id, @MotorModel, @MotorSerialNumber, @TestTime, @ProductionTime,
                     @RatedPower, @RatedVoltage, @RatedCurrent, @RatedFrequency,
                     @RatedTorque, @RatedSpeed, @NominalEfficiency, @RatedPowerFactor,
                     @RotationalInertia, @Series, @WorkSystem, @ConnectionMethod,
                     @InsulationClass, @ProtectionLevel, @TemperatureRiseClass,
                     @K1, @K2, @SelectedStandard, @TestResultStatus)";

                int rows = await _dbService.ExecuteAsync(sql, Testrecord);

                if (rows > 0)
                {
                    _Logger.Information($"试验任务 [{Testrecord.Id}] 已成功写入数据库");                    
                    return true;                   
                }
                else
                {
                    _Logger.Information("数据库写入返回 0 行，请检查！");
                    return false;
                }
            }
            catch (Exception ex)
            {                
                _Logger.Error(ex, "试验建档写入数据库失败");               
                return false;
            }
        }

        public async Task<bool> UpdateTestRecordAsync(MotorTestRecord Testrecord)
        {
            try
            {
                // ==================== 更新数据库 (使用 UPDATE 语句) ====================
                // 注意：Id 是主键，不能被更新，所以放在 WHERE 条件里。
                // MotorSerialNumber (出厂条码) 通常也不允许修改，这里也排除了。如果你的业务允许修改条码，可以在 SET 里加上 MotorSerialNumber = @MotorSerialNumber
                string sql = @"UPDATE motor_test_record 
                               SET MotorModel = @MotorModel, 
                                   TestTime = @TestTime, 
                                   ProductionTime = @ProductionTime,
                                   RatedPower = @RatedPower, 
                                   RatedVoltage = @RatedVoltage, 
                                   RatedCurrent = @RatedCurrent, 
                                   RatedFrequency = @RatedFrequency,
                                   RatedTorque = @RatedTorque, 
                                   RatedSpeed = @RatedSpeed, 
                                   NominalEfficiency = @NominalEfficiency, 
                                   RatedPowerFactor = @RatedPowerFactor,
                                   RotationalInertia = @RotationalInertia, 
                                   Series = @Series, 
                                   WorkSystem = @WorkSystem, 
                                   ConnectionMethod = @ConnectionMethod,
                                   InsulationClass = @InsulationClass, 
                                   ProtectionLevel = @ProtectionLevel, 
                                   TemperatureRiseClass = @TemperatureRiseClass,
                                   K1 = @K1, 
                                   K2 = @K2, 
                                   SelectedStandard = @SelectedStandard, 
                                   TestResultStatus = @TestResultStatus
                             WHERE Id = @Id";

                int rows = await _dbService.ExecuteAsync(sql, Testrecord);

                if (rows > 0)
                {
                    _Logger.Information($"试验任务 [{Testrecord.Id}] 铭牌参数已成功更新");
                    return true;
                }
                else
                {
                    _Logger.Warning($"更新失败：未找到流水号为 [{Testrecord.Id}] 的记录，或数据未发生任何改变。");
                    return false; // 注意这里去掉了 Growl 弹窗，弹窗应该放在 ViewModel 里做，仓储只负责返回 true/false
                }
            }
            catch (Exception ex)
            {
                // 1. 记录底层详细日志
                _Logger.Error(ex, $"试验任务 [{Testrecord.Id}] 铭牌参数更新时发生数据库异常");

                // 2. 原封不动地把这个异常抛给上层！让上层的 catch 去处理。
                throw;
            }
        }

        public async Task<DcResistanceRecord?> GetDcResistanceByRecordIdAsync(string recordId)
        {
            string sql = "SELECT * FROM motor_dc_resistance WHERE RecordId = @RecordId LIMIT 1;";
            try
            {
                return await _dbService.QueryFirstOrDefaultAsync<DcResistanceRecord>(sql, new { RecordId = recordId });
            }
            catch (Exception ex)
            {
                _Logger.Error($"试验 [{recordId}]的直流电阻保存/更新数据库异常，原因:" + ex);
                throw;
            }         
        }

        public async Task<bool> SaveDcResistanceAsync(DcResistanceRecord record)
        {
            try
            {
                string sql = @"INSERT INTO motor_dc_resistance 
                            (RecordId,Ruv,Rvw,Rwu,WindingTemp,AverageResistance,UnbalanceRate,ResistanceAt20C,IsQualified,TestTime)
                            VALUES 
                            (@RecordId, @Ruv, @Rvw, @Rwu, @WindingTemp, @AverageResistance, @UnbalanceRate, @ResistanceAt20C, @IsQualified, @TestTime)
                            ON DUPLICATE KEY UPDATE 
        Ruv=@Ruv, Rvw=@Rvw, Rwu=@Rwu, WindingTemp=@WindingTemp,
        AverageResistance=@AverageResistance, UnbalanceRate=@UnbalanceRate,
        ResistanceAt20C=@ResistanceAt20C, IsQualified=@IsQualified, TestTime=@TestTime;";

                int rows = await _dbService.ExecuteAsync(sql, record);
                if (rows > 0)
                {
                    _Logger.Information($"试验 [{record.RecordId}]的直流电阻保存/更新数据库成功");
                    return true;
                }
                else
                {
                    _Logger.Warning($"试验 [{record.RecordId}]的直流电阻保存/更新数据库失败");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _Logger.Error($"试验 [{record.RecordId}]的直流电阻保存/更新数据库异常，原因:"+ ex);
                throw;
            }
        }

        
    }
}
