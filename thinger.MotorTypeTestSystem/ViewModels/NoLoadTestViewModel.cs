using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using thinger.MotorTypeTestSystem.Models;
using thinger.MotorTypeTestSystem.Models.NoLoadTestModels;
using thinger.MotorTypeTestSystem.Models.TestHelpers;
using thinger.MotorTypeTestSystem.Services;

namespace thinger.MotorTypeTestSystem.ViewModels
{
    public partial class NoLoadTestViewModel : ObservableObject, ISubTestViewModel
    {
        private readonly IMotorTestRepository _repo;
        private readonly ILogger _logger;
        private readonly TagPoolService _tagPool; // 实时采集数据池
        private readonly ITempChannelService _tempChannelService; // 注入测温通道服务

        //保存从主控台传过来的电机铭牌（含电机的流水号 Id、材料常数 K1 等）
        [ObservableProperty]
        private MotorTestRecord? _motorRecord;

        //空载试验的最终计算结果与总记录(数据也会绑定在界面)
        [ObservableProperty]
        private NoLoadTestRecord _noLoadRecord = new();

        //绑到前端 DataGrid 的采集测试点数据集合 
        [ObservableProperty]
        private ObservableCollection<NoLoadPointModel> _noLoadTestPoints = new();

        #region 图表事件（通知 View 刷新 ScottPlot 画布）
        /// <summary>
        /// 请求前端刷新国标曲线
        /// </summary>
        public event Action? RequestDrawPlots;
        /// <summary>
        /// 请求清空图表事件
        /// </summary>
        public event Action? RequestClearPlots;
        /// <summary>
        /// 请求前端将空载曲线导出为 PNG 高清截图（传入完整路径，返回是否成功）
        /// </summary>
        public event Func<string, bool>? RequestExportChartImage;
        #endregion

        public NoLoadTestViewModel(IMotorTestRepository motorTestRepository, ILogger logger, TagPoolService tagPool, ITempChannelService tempChannelService)
        {
            _repo = motorTestRepository;
            _tagPool = tagPool;
            _logger = logger;
            _tempChannelService = tempChannelService;
        }

        #region 切换至空载试验界面执行的方法 InitializeTestRecord
        public async void InitializeTestRecord(MotorTestRecord mainRecord)
        {
            if (mainRecord == null) return;
            MotorRecord = mainRecord;
            try
            {
                var existingRecord = await _repo.GetNoLoadTestRecordByRecordIdAsync(mainRecord.Id);
                if (existingRecord != null)
                {
                    // 查到了！自动把之前存好的数据回显到界面上
                    NoLoadRecord = existingRecord;
                    _logger.Information($"已自动加载电机 [{mainRecord.Id}] 的历史空载试验记录");
                    if (!string.IsNullOrWhiteSpace(existingRecord.PointsJson))
                    {
                        var points = JsonSerializer.Deserialize<List<NoLoadPointModel>>(existingRecord.PointsJson);
                        if (points != null && points.Count > 0)
                        {
                            NoLoadTestPoints = new ObservableCollection<NoLoadPointModel>(points);//new一次是将list变为ObservableCollection可以显示刷新在UI界面
                        }
                    }
                    // 触发前端画出历史曲线
                    RequestDrawPlots?.Invoke();
                }
                else
                {
                    // 没查到，说明是新试验，初始化一个干净的实体，并把外键 RecordId 绑上
                    NoLoadRecord = new NoLoadTestRecord
                    {
                        RecordId = mainRecord.Id
                    };
                    NoLoadTestPoints.Clear();
                    RequestClearPlots?.Invoke();

                    // 查看这台电机是否已经做过【直流电阻试验】
                    var dcRecord = await _repo.GetDcResistanceByRecordIdAsync(mainRecord.Id);
                    if (dcRecord != null && dcRecord.AverageResistance > 0)
                    {
                        NoLoadRecord.ColdResistance = dcRecord.AverageResistance;
                        NoLoadRecord.ColdResistanceTemp = dcRecord.WindingTemp;
                        _logger.Information($"已自动继承电机 [{mainRecord.Id}] 冷态电阻：R1={dcRecord.AverageResistance:F5}Ω, θ1={dcRecord.WindingTemp:F1}℃");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "加载空载试验历史数据失败");
                Growl.Error($"加载空载试验历史数据失败：{ex.Message}");
            }
        }
        #endregion

        #region 根据当前冷态电阻基准 R1、θ1，计算单个测试点的 R0(空载试验每个电压点定子绕组端电阻)、铜耗 Pcu0 与净损耗 Pc
        /// <summary>
        /// 根据当前冷态电阻基准 R1、θ1，计算单个测试点的 R0、铜耗 Pcu0 与净损耗 Pc
        /// </summary>
        private void RecalculatePointLoss(NoLoadPointModel pt)
        {
            // 电压标幺值 U0 / UN
            double un = MotorRecord?.RatedVoltage > 0 ? MotorRecord.RatedVoltage : 380.0;
            pt.VoltageRatio = Math.Round(pt.Voltage / un, 4);
            // 绕组材料常数 K1（铜 235，铝 225）
            double k1 = MotorRecord?.K1 > 0 ? MotorRecord.K1 : 235.0;
            double r1 = NoLoadRecord.ColdResistance;
            double t1 = NoLoadRecord.ColdResistanceTemp;
            if (r1 > 0 && (k1 + t1) > 0)
            {
                // 1. 折算到当前绕组温度 θ0 下的定子线电阻：R0 = R1 * (K1 + θ0) / (K1 + θ1)
                pt.StatorResistance = Math.Round(r1 * (k1 + pt.WindingTemp) / (k1 + t1), 5);
                // 2. 三相定子铜耗（线电阻体系）：Pcu0 = 1.5 * I0² * R0
                pt.StatorCopperLoss = Math.Round(1.5 * pt.Current * pt.Current * pt.StatorResistance, 2);
                // 3. 扣除铜耗后的恒定损耗（净损耗）：Pc = P0 - Pcu0
                pt.NetLoss = Math.Round(Math.Max(0, pt.InputPower - pt.StatorCopperLoss), 2);
            }
            else
            {
                // 还没填冷态电阻时，先不扣铜耗
                pt.StatorResistance = 0;
                pt.StatorCopperLoss = 0;
                pt.NetLoss = pt.InputPower;
            }
        }
        #endregion      

        #region 采集数据点,删除某一条误采的测试点,清空所有测试点并重置图表, 按实测电压从高到低排序，并重新编排 1, 2, 3... 序号等功能

        /// <summary>
        /// 自由采点：点击【采入当前工况点】，从数据池抓取实时数据并追加到表格
        /// </summary>
        [RelayCommand]
        private void AddSamplePoint()
        {
            // 1. 从数据池读三相线电压，求算术平均 U0
            double uab = Math.Round(_tagPool.GetValue<double>("IO.Voltage_Uab"), 2);
            double ubc = Math.Round(_tagPool.GetValue<double>("IO.Voltage_Ubc"), 2);
            double uca = Math.Round(_tagPool.GetValue<double>("IO.Voltage_Uca"), 2);
            double uAvg = Math.Round((uab + ubc + uca) / 3.0, 2);
            if (uAvg <= 0)
            {
                Growl.Warning("当前实测电压为 0，请确认电源已开启且功率分析仪通信正常！");
                return;
            }

            // 2. 读三相线电流，求平均 I0 与国标电流不平衡度（最大相偏差 / 平均值 * 100%）
            double ia = Math.Round(_tagPool.GetValue<double>("IO.Current_Ia"), 3);
            double ib = Math.Round(_tagPool.GetValue<double>("IO.Current_Ib"), 3);
            double ic = Math.Round(_tagPool.GetValue<double>("IO.Current_Ic"), 3);
            double iAvg = Math.Round((ia + ib + ic) / 3.0, 3);
            double unbalance = 0;
            if (iAvg > 0)//这里开始求三相电流不平衡度
            {
                double maxDiff = Math.Max(Math.Abs(ia - iAvg), Math.Max(Math.Abs(ib - iAvg), Math.Abs(ic - iAvg)));
                unbalance = Math.Round((maxDiff / iAvg) * 100.0, 2);
            }

            // 3. 读功率、功率因数、频率、转速
            double p0 = Math.Round(_tagPool.GetValue<double>("P_active"), 2);
            double pf = Math.Round(_tagPool.GetValue<double>("PowerFactor"), 4);
            double freq = Math.Round(_tagPool.GetValue<double>("Frequency"), 2);
            if (freq <= 0) freq = 50.0;
            double speed = Math.Round(_tagPool.GetValue<double>("IO.PLC_Speed"), 1);
            int poles = MotorRecord?.Series > 0 ? MotorRecord.Series : 4; // 极数，默认 4 极
            double ns = 120.0 * freq / poles;                             // 同步转速 ns = 120f / 2p = 60f/p，p是极对数也就是极数除以2
            double slip = ns > 0 ? Math.Round((ns - speed) / ns, 4) : 0;  // 转差率 s = (ns - n) / ns

            // 4.赋值给一个新实体点
            var pt = new NoLoadPointModel
            {
                UA = uab,
                UB = ubc,
                UC = uca,
                Voltage = uAvg,
                IA = ia,
                IB = ib,
                IC = ic,
                Current = iAvg,
                CurrentUnbalance = unbalance,
                InputPower = p0,
                PowerFactor = pf,
                Frequency = freq,
                Speed = speed,
                Slip = slip,
                WindingTemp = GetCurrentWindingTemp(),
                SampleTime = DateTime.Now
            };
            // 5. 算出这个点的 VoltageRatio、R0、铜耗 Pcu0 和净损耗 Pc
            RecalculatePointLoss(pt);
            // 6. 加入列表并按电压从高到低自动排序 + 重新编序号
            NoLoadTestPoints.Add(pt);
            SortAndReindexPoints();
            Growl.Success($"已采入第 {NoLoadTestPoints.Count} 个点：U₀={pt.Voltage:F1}V ({pt.VoltageRatio:P1})，I₀={pt.Current:F3}A");
        }

        /// <summary>
        /// 删除某一条误采的测试点
        /// </summary>
        [RelayCommand]
        private void DeletePoint(NoLoadPointModel? pt)
        {
            if (pt == null) return;
            NoLoadTestPoints.Remove(pt);
            SortAndReindexPoints();
        }
        /// <summary>
        /// 清空所有测试点并重置图表
        /// </summary>
        [RelayCommand]
        private void ClearAllPoints()
        {
            NoLoadTestPoints.Clear();
            RequestClearPlots?.Invoke();
            Growl.Info("已清空全部空载测试点");
        }

        /// <summary>
        /// 按实测电压从高到低排序，并重新编排 1, 2, 3... 序号
        /// </summary>
        private void SortAndReindexPoints()
        {
            var sorted = NoLoadTestPoints.OrderByDescending(p => p.Voltage).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                sorted[i].PointIndex = i + 1;
            }
            NoLoadTestPoints = new ObservableCollection<NoLoadPointModel>(sorted);
        }
        #endregion

        #region 从巡检仪实时采集当前定子绕组温度 θ₀ (℃)
        /// <summary>
        /// 从巡检仪实时采集当前定子绕组温度 θ₀ (℃)
        /// 优先级：已启用的“绕组”通道平均值 -> “机壳”通道 -> 界面环境温度兜底
        /// </summary>
        private double GetCurrentWindingTemp()
        {
            var windingTemps = new List<double>();
            double shellTemp = 0;

            foreach (var ch in _tempChannelService.Channels)
            {
                if (!ch.IsEnabled) continue;
                double val = _tagPool.GetValue<double>(ch.TagName);
                if (val <= -50 || val == 0) continue; // 过滤掉未接传感器/断线的无效值

                // 刷新一下当前实时环境温度
                if (ch.IsAmbient || ch.PointType == "环境基准")
                {
                    NoLoadRecord.AmbientTemp = Math.Round(val, 1);
                    continue;
                }

                // 收集所有绕组测点（如 "绕组"、"绕组PT100"）
                if (!string.IsNullOrWhiteSpace(ch.PointType) && ch.PointType.Contains("绕组"))
                {
                    windingTemps.Add(val);
                }
                else if (ch.PointType == "机壳" && val > shellTemp)
                {
                    shellTemp = val;
                }
            }
            // 1. 优先返回所有有效绕组测点的平均值
            if (windingTemps.Count > 0)
                return Math.Round(windingTemps.Average(), 1);
            // 2. 其次返回机壳温度
            if (shellTemp > 0)
                return Math.Round(shellTemp, 1);
            // 3. 都没接巡检仪时，兜底返回环境温度
            return NoLoadRecord.AmbientTemp > 0 ? NoLoadRecord.AmbientTemp : 25.0;
        }
        #endregion

        #region 国标损耗分离计算与保存入库
        /// <summary>
        /// 点击【立即计算】：全表重算铜耗 -> 调用 Helper 分离损耗 -> 通知 View 绘制空载试验特性曲线
        /// </summary>
        [RelayCommand]
        private void Calculate()
        {

            //调用写好的国标算法引擎（自动校验：总数>=7、低压>=3、高压>=0.95）
            var (success, message) = TryCalculateCore();
            if (!success)
            {
                Growl.Warning(message);
                return;
            }
            // 通知 View 绘制空载试验特性曲线
            RequestDrawPlots?.Invoke();
            Growl.Success($"{message}（Pfw={NoLoadRecord.MechanicalLoss:F1}W，Pfe={NoLoadRecord.IronLoss:F1}W，R²={NoLoadRecord.RegressionR2:F4}）");
        }

        /// <summary>
        /// 点击【保存数据】：校验 -> 核算 -> 序列化点位 JSON -> 写入 MySQL
        /// </summary>
        [RelayCommand]
        private async Task Save()
        {
            if (MotorRecord == null || string.IsNullOrWhiteSpace(NoLoadRecord.RecordId))
            {
                Growl.Warning("未关联有效的试验主记录，无法保存！");
                return;
            }
            var (success, message) = TryCalculateCore();
            if (!success)
            {
                Growl.Warning($"保存前核算未通过：{message}");
                return;
            }

            try
            {
                NoLoadRecord.TestTime = DateTime.Now;

                //空载试验曲线截图与温升试验保持统一目录：程序目录\Data\Curves\NoLoad_{流水号}.png
                string curvesFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Curves");
                if (!System.IO.Directory.Exists(curvesFolder))
                {
                    System.IO.Directory.CreateDirectory(curvesFolder);
                }
                string imageFilePath = System.IO.Path.Combine(curvesFolder, $"NoLoad_{NoLoadRecord.RecordId}.png");
                bool imageSaved = RequestExportChartImage?.Invoke(imageFilePath) ?? false;
                if (imageSaved)
                {
                    NoLoadRecord.CurveImagePath = imageFilePath;
                    _logger.Information($"空载特性曲线截图已自动存盘：{imageFilePath}");
                }

                NoLoadRecord.PointsJson = JsonSerializer.Serialize(NoLoadTestPoints);
                bool saved = await _repo.SaveNoLoadTestRecordAsync(NoLoadRecord);
                if (saved)
                {
                    RequestDrawPlots?.Invoke();
                    Growl.Success("空载型式试验数据保存成功！");
                    _logger.Information($"试验 [{NoLoadRecord.RecordId}] 空载数据保存成功，共 {NoLoadTestPoints.Count} 个点");
                }
                else
                {
                    Growl.Error("空载数据保存失败，请检查数据库连接！");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "保存空载试验数据时发生异常");
                Growl.Fatal($"保存失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 重新计算每个点位的铜耗和净损耗等然后再进行国标算法计算出风摩耗以及铁耗等数据
        /// </summary>
        /// <returns></returns>
        private (bool Success, string Message) TryCalculateCore()
        {
            foreach (var pt in NoLoadTestPoints)
            {
                RecalculatePointLoss(pt);
            }
            double un = MotorRecord?.RatedVoltage > 0 ? MotorRecord.RatedVoltage : 380.0;
            double inRated = MotorRecord?.RatedCurrent > 0 ? MotorRecord.RatedCurrent : 1.0;

            return NoLoadCurveHelper.CalculateLossSeparation(
                NoLoadTestPoints.ToList(),
                un,
                inRated,
                NoLoadRecord);
        }
        #endregion
    }
}
