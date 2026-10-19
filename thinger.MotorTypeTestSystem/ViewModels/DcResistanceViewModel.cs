using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using thinger.Mexico50Towdolly.ModbusRTU;
using thinger.MotorTypeTestSystem.Models;
using thinger.MotorTypeTestSystem.Services;

namespace thinger.MotorTypeTestSystem.ViewModels
{
    public partial class DcResistanceViewModel : ObservableObject, ISubTestViewModel
    {
        private readonly IMotorTestRepository _repo;
        private readonly ILogger _logger;
        private readonly IDeviceManagerService _deviceManager; //注入设备管理器

        //保存从主控台传过来的电机铭牌（含电机的流水号 Id、材料常数 K1 等）
        [ObservableProperty]
        private MotorTestRecord? _motor_record;

        //  绑定界面的直流电阻数据模型（三相电阻、温度、不平衡率等）
        [ObservableProperty]
        private DcResistanceRecord _dcRecord = new();


        public DcResistanceViewModel(IMotorTestRepository repo, ILogger logger, IDeviceManagerService deviceManagerService)
        {
            _repo = repo;
            _logger = logger;
            _deviceManager = deviceManagerService;
        }

        #region 当切换到本页面时，总控台自动把电机的建档信息传进来
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
        #endregion

        #region 点击【计算数据】按钮触发(按照国家标准 GB/T 1032 进行精确数学换算)
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
            // 4. 自动合格判定 (国标通常规定：默认三相不平衡度 <=2.0% 为合格)
            DcRecord.IsQualified = DcRecord.UnbalanceRate <= DcRecord.MaxAllowedUnbalance;
        }
        #endregion

        #region 点击保存数据按钮（保存前会自动先计算一次，确保数据最新，然后存入数据库）
        /// <summary>
        /// 命令二：点击【保存数据】按钮触发
        /// 保存前会自动先计算一次，确保数据最新，然后存入数据库
        /// </summary>
        [RelayCommand]
        public async Task Save()
        {
            if (Motor_record == null || string.IsNullOrWhiteSpace(DcRecord.RecordId))
            {
                Growl.Warning("未关联有效的试验主记录，无法保存！");
                return;
            }

            // 1. 保存前强制触发一次自动计算，防呆防漏
            Calculate();

            // 2. 检查是否有数据
            if (DcRecord.Ruv <= 0 || DcRecord.Rvw <= 0 || DcRecord.Rwu <= 0)
            {
                Growl.Warning("三相线电阻数据必须大于 0，请检查输入！");
                return;
            }

            try
            {
                DcRecord.TestTime = DateTime.Now;
                // 3. 调用仓储层的保存/更新接口
                bool success = await _repo.SaveDcResistanceAsync(DcRecord);
                if (success)
                {
                    Growl.Success("直流电阻试验数据保存成功！");
                    _logger.Information($"电机 [{DcRecord.RecordId}] 直流电阻试验数据保存成功");
                }
                else
                {
                    Growl.Error("直流电阻数据保存失败，请检查数据库连接！");
                }

            }
            catch (Exception ex)
            {

                _logger.Error(ex, "保存直流电阻数据时发生异常");
                Growl.Fatal($"保存失败：{ex.Message}");
            }
        }
        #endregion

        #region 点击按钮采集电桥的数据赋值给三个线电阻
        /// <summary>
        /// 命令三：点击各相的【采入】按钮触发
        /// 根据传入的参数 phase ("UV", "VW", "WU") 采集当前电桥数值
        [RelayCommand]
        private async Task SampleResistance(string phase)
        {
            if (DcRecord == null || string.IsNullOrWhiteSpace(phase)) return;

            try
            {
                // 1. 从设备管理器拿到电桥的配置
                var bridgeConfig = _deviceManager.DeviceConfigs.Find(d => d.DeviceId == "直流电阻仪");
                if (bridgeConfig == null)
                {
                    Growl.Warning("未找到电桥设备配置 (Bridge_1)，请检查 devices.json！");
                    return;
                }
                // 2. 拿到电桥的通讯服务
                object service = ConnectionManager.GetService(bridgeConfig);
                if (service == null)
                {
                    Growl.Warning("电桥尚未连接，请先在硬件配置中连接电桥！");
                    return;
                }

                double currentVal = 0;

                if (service is ModbusRTUService rtu && rtu.IsConnected)
                {
                    // 假设电桥保持寄存器从 0 开始，长度为 2 (Float 占 2 个寄存器)
                    ushort[] raw = await rtu.ReadHoldingRegistersAsync(bridgeConfig.SlaveId, 0, 2);
                    if (raw != null && raw.Length >= 2)
                    {
                        if (CustomModbusParsers.Parsers.TryGetValue("电桥_毫欧转欧姆", out var parseFunc))
                        {
                            currentVal = Convert.ToDouble(parseFunc(raw, 0));
                        }
                    }
                }
                else if (service is ModbusTCPService tcp && tcp.IsConnected)
                {
                    ushort[] raw = await tcp.ReadHoldingRegistersAsync(0, 2);
                    if (raw != null && raw.Length >= 2)
                    {
                        //此解析方式为自带，如有解析方式可更换
                        currentVal = tcp.RegistersToFloat(raw, 0, ModbusTCPService.ModbusFloatByteOrder.BigEndian);
                    }
                }
                if (currentVal <= 0)
                {
                    Growl.Warning("采集到的电阻值为 0 或未读取到有效数值，请检查接线！");
                    return;
                }

                // 4. 格式化并赋值给对应的相
                currentVal = Math.Round(currentVal, 5);
                switch (phase.ToUpper())
                {
                    case "UV":
                        DcRecord.Ruv = currentVal;
                        Growl.Success($"已采入 U-V 阻值：{currentVal} Ω");
                        break;
                    case "VW":
                        DcRecord.Rvw = currentVal;
                        Growl.Success($"已采入 V-W 阻值：{currentVal} Ω");
                        break;
                    case "WU":
                        DcRecord.Rwu = currentVal;
                        Growl.Success($"已采入 W-U 阻值：{currentVal} Ω");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"单次采入 {phase} 线电阻失败");
                Growl.Error($"采集失败：{ex.Message}");
            }
        }
        #endregion
    }
}
