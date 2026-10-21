using Microsoft.Extensions.Configuration;
using Prism.DryIoc;
using Prism.Ioc;
using Serilog;
using System;
using System.IO;
using System.Windows;
using thinger.MotorTypeTestSystem.Services;
using thinger.MotorTypeTestSystem.ViewModels;
using thinger.MotorTypeTestSystem.Views;
using Thinger.DatabaseOperationlibrary;

namespace thinger.MotorTypeTestSystem
{
    public partial class App : PrismApplication
    {

        // 声明一个全局的配置接口对象
        private IConfiguration _configuration;

        /// <summary>
        /// 1. 构造函数：软件双击运行的第一瞬间，先读配置文件
        /// </summary>
        public App()
        {
            _configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // 1. 初始化 Serilog
            string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File(
                    path: Path.Combine(logDirectory, "log-.txt"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .CreateLogger();

            Log.Information("=== 电机型式试验系统启动 ===");

            base.OnStartup(e);

            // 2. 第一道防线：UI 主线程未捕获异常
            DispatcherUnhandledException += (s, args) =>
            {
                Log.Error(args.Exception, "UI 主线程发生未捕获异常！");
                args.Handled = true;
                HandyControl.Controls.Growl.Error($"系统运行异常：{args.Exception.Message}");
            };

            // 3. 第二道防线：Task 异步线程未捕获异常
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                Log.Error(args.Exception, "异步任务 (Task) 发生未观察到的异常！");
                args.SetObserved();
            };

            // 4. 第三道防线：致命托底异常
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                Log.Fatal(ex, "应用程序发生致命未捕获异常，进程即将终止！");
                MessageBox.Show($"系统发生致命错误，即将退出：{ex?.Message}", "致命错误", MessageBoxButton.OK, MessageBoxImage.Error);
            };
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // (1) 把 IConfiguration 本身注册进容器（以后哪个 ViewModel 想读配置，直接在构造函数要 IConfiguration 即可）
            containerRegistry.RegisterInstance<IConfiguration>(_configuration);

            // (2) 注册官方 Serilog 单例实例
            containerRegistry.RegisterInstance<ILogger>(Log.Logger);

            containerRegistry.RegisterSingleton<TagPoolService>();//注册数据池为单例
            containerRegistry.RegisterSingleton<IDataAcquisitionService, DataAcquisitionService>();//注册数据采集服务

            // (3) 核心：从配置中拿到连接字符串，实例化数据库服务并注册
            string connectionString = _configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("未在 appsettings.json 中找到 DefaultConnection！");

            var dbService = new MySqlDatabaseService(connectionString, Log.Logger);
            containerRegistry.RegisterInstance<IDatabaseService>(dbService);
            Log.Information("数据库服务注册完成，等待调用");

            // 注册设备管理服务为单例(RegisterInstance是已经造好了对象，交给容器保管。RegisterSingleton是告诉容器怎么造对象，让容器去造，造好了保管起来)
            containerRegistry.RegisterSingleton<IDeviceManagerService, DeviceManagerService>();
            containerRegistry.RegisterSingleton<IHardwareManagerService, HardwareManagerService>();
            containerRegistry.RegisterSingleton<IMotorTestRepository, MotorTestRepository>();

            // 2. 注册核心功能页面 (Prism Navigation)
            containerRegistry.RegisterForNavigation<HardwareConfigView, HardwareConfigViewModel>();
            containerRegistry.RegisterForNavigation<CreateTestView, CreateTestViewModel>();
            containerRegistry.RegisterForNavigation<TestExecutionView, TestExecutionViewModel>();
            containerRegistry.RegisterForNavigation<NamePlateView, NamePlateViewModel>();
            containerRegistry.RegisterForNavigation<DcResistanceView, DcResistanceViewModel>();
            containerRegistry.RegisterForNavigation<HistoryQueryView, HistoryQueryViewModel>();
        }

        protected override Window CreateShell()
        {
            return Container.Resolve<MainWindow>();
        }

        //它的触发时机是：所有服务都已经登记完毕，容器已经彻底搭建完成，主窗口即将打开。
        protected override void OnInitialized()
        {
            base.OnInitialized();
            try
            {
                // 0. 数据库连接自检(防止因数据库连接不上导致存储的时候才发现)
                _ = InitializeDatabaseAsync();

                // 1. 从容器中解析设备管理器（此时容器会自动帮它把 ILogger 注入进去）
                var deviceManager = Container.Resolve<IDeviceManagerService>();

                // 2. 执行读取 JSON 配置文件
                deviceManager.InitializeConfigs();

                var HardwareManagerService = Container.Resolve<IHardwareManagerService>();
                HardwareManagerService.AutoConnectAllAsync();

                // 3. 启动数据采集引擎
                var dataAcqService = Container.Resolve<IDataAcquisitionService>();
                dataAcqService.Start();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "系统初始化关键服务失败！");
                MessageBox.Show("硬件配置初始化失败，请查看日志！", "启动错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 启动时自动测试数据库连接是否正常
        /// </summary>
        private async Task InitializeDatabaseAsync()
        {
            try
            {
                var dbService = Container.Resolve<IDatabaseService>();

                // 这一步如果底层抛出异常，会直接跳到下面的 catch 块
                bool isConnected = await dbService.TestConnectionAsync();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (isConnected)
                    {
                        Log.Information("数据库连接自检成功！");
                        HandyControl.Controls.Growl.Success("数据库自检成功，系统准备就绪！");
                    }
                });
            }
            catch (Exception ex) // 底层的异常会在这里被集中处理！
            {
                // 这里统一记录错误日志，并带上底层传上来的真实错误原因（ex.Message）
                Log.Error(ex, "数据库连接自检异常");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    HandyControl.Controls.Growl.Fatal(new HandyControl.Data.GrowlInfo
                    {
                        Message = $"数据库连接失败！请检查 MySQL 服务或网络配置。\n原因：{ex.Message}",
                        ShowDateTime = true,
                        WaitTime = 0
                    });
                });
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            var HardwareManagerService = Container.Resolve<IHardwareManagerService>();
            HardwareManagerService.DisconnectAllAsync().Wait();//所有设备断开连接
            var dataAcqService = Container.Resolve<IDataAcquisitionService>();
            dataAcqService.Stop();//关闭轮询读取
            Log.Information("=== 电机型式试验系统正常退出 ===");
            Log.CloseAndFlush(); // 释放并落盘

            base.OnExit(e);
        }
    }
}