using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using Serilog;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using thinger.MotorTypeTestSystem.Models;
using thinger.MotorTypeTestSystem.Services;

namespace thinger.MotorTypeTestSystem.ViewModels
{
    public partial class NamePlateViewModel : ObservableObject, ISubTestViewModel
    {
        // 用来绑定界面的电机铭牌信息
        [ObservableProperty]
        private MotorTestRecord _motor_record;

        private readonly IMotorTestRepository _motorTestRepo;
        private readonly ILogger _logger;
        public NamePlateViewModel(IMotorTestRepository motorTestRepository,ILogger logger)
        {
            _motorTestRepo = motorTestRepository;
            _logger = logger;
        }

        /// <summary>
        /// 试验中心传递来的电机信息的接口实现
        /// </summary>
        /// <param name="mainRecord"></param>
        /// <exception cref="NotImplementedException"></exception>
        public void InitializeTestRecord(MotorTestRecord mainRecord)
        {
            Motor_record = mainRecord;
        }

        [RelayCommand]
        private async Task SaveChanges()
        {
            if (Motor_record == null) return; // 防呆检查
            try
            {
                bool success = await _motorTestRepo.UpdateTestRecordAsync(Motor_record);
                if (success)
                {
                    Growl.Success($"修改铭牌信息已完成！");
                }
                else
                {
                    Growl.Info($"更新失败：未找到ID为 [{Motor_record.Id}] 的记录，或数据未发生任何改变。");
                }
            }
            catch (Exception ex)
            {
                // 1. 记录前端层面的日志
                _logger.Error(ex, $"更新 {Motor_record.Id} 铭牌信息失败");

                // 2. 【核心】把真正的错误原因弹窗显示给用户看！
                Growl.Fatal($"保存失败，发生系统异常：\n{ex.Message}");
            }          
        }
    }
}
