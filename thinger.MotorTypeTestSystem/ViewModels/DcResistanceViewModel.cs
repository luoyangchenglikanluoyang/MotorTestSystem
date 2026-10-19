using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using thinger.MotorTypeTestSystem.Models;
using thinger.MotorTypeTestSystem.Services;

namespace thinger.MotorTypeTestSystem.ViewModels
{
    public partial class DcResistanceViewModel : ObservableObject, ISubTestViewModel
    {
        private readonly IMotorTestRepository _repo;
        private readonly ILogger _logger;

        //保存从主控台传过来的电机铭牌（含电机的流水号 Id、材料常数 K1 等）
        [ObservableProperty]
        private MotorTestRecord? _motor_record;

        //  绑定界面的直流电阻数据模型（三相电阻、温度、不平衡率等）
        [ObservableProperty]
        private DcResistanceRecord _dcRecord = new();


        public DcResistanceViewModel(IMotorTestRepository repo, ILogger logger)
        {
            _repo = repo;
            _logger = logger;
        }

        /// <summary>
        /// 当切换到本页面时，总控台自动把电机的建档信息传进来
        /// </summary>
        [RelayCommand]
        public async void InitializeTestRecord(MotorTestRecord mainRecord)
        {
            if (mainRecord == null) return;
            Motor_record = mainRecord;
            try
            {
                // 去数据库查一下：这台电机之前是不是已经做过直流电阻试验了？
                var existingRecord = await _repo.GetDcResistanceByRecordIdAsync(mainRecord.Id);
                if (existingRecord != null)
                {
                    // 查到了！自动把之前存好的数据回显到界面上
                    DcRecord = existingRecord;
                    _logger.Information($"已自动加载电机 [{mainRecord.Id}] 的历史直流电阻记录");
                }
                else
                {
                    // 没查到，说明是新试验，初始化一个干净的实体，并把外键 RecordId 绑上
                    DcRecord = new DcResistanceRecord
                    {
                        RecordId = mainRecord.Id,
                        WindingTemp = 25.0 // 默认给一个标准室温 25℃
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "加载直流电阻试验历史数据失败");
            }
        }

        /// <summary>
        /// 命令一：点击【计算数据】按钮触发
        /// 按照国家标准 GB/T 1032 进行精确数学换算
        /// </summary>
        [RelayCommand]
        private void Calculate()
        {
            // 防呆校验：三相电阻必须大于 0 才能算
            if (DcRecord.Ruv <= 0 || DcRecord.Rvw <= 0 || DcRecord.Rwu <= 0)
            {
                return;
            }

            // 1. 计算三相平均电阻 R_avg = (Ruv + Rvw + Rwu) / 3
            double avg = (DcRecord.Ruv + DcRecord.Rvw + DcRecord.Rwu) / 3.0;
            DcRecord.AverageResistance = Math.Round(avg, 5); // 保留 5 位小数

            // 2. 计算三相不平衡度 = (最大值 - 最小值) / 平均值 * 100%
            double max = Math.Max(DcRecord.Ruv, Math.Max(DcRecord.Rvw, DcRecord.Rwu));
            double min = Math.Min(DcRecord.Ruv, Math.Min(DcRecord.Rvw, DcRecord.Rwu));
            DcRecord.UnbalanceRate = Math.Round(((max - min) / avg) * 100.0, 3); // 保留 3 位百分比
            // 3. 折算至 20℃ 时的标准基准电阻：
            // 国标公式：R_20 = R_avg * (K1 + 20) / (K1 + T_实测)
            // 绝大部分电机是铜线(K1=235)，铝线(K1=225)。直接读取铭牌里存的 K1！
            double k1 = Motor_record?.K1 > 0 ? Motor_record.K1 : 235.0;
            DcRecord.ResistanceAt20C = Math.Round(avg * (k1 + 20.0) / (k1 + DcRecord.WindingTemp), 5);
            // 4. 自动合格判定 (国标通常规定：三相不平衡度 <= 2.0% 为合格)
            DcRecord.IsQualified = DcRecord.UnbalanceRate <= 2.0;
        }
    }
}
