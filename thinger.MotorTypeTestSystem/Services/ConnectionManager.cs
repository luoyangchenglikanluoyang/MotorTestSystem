using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using thinger.Mexico50Towdolly.ModbusRTU;
using thinger.MotorTypeTestSystem.Models.Hardware;

namespace thinger.MotorTypeTestSystem.Services
{
    /// <summary>
    /// 全局通信连接管家：自动处理设备共享与独立分配
    /// </summary>
    public static class ConnectionManager
    {
        // 内部小本子，用来记录【服务实例】和【正在使用的次数】
        private class ConnectionInfo
        {
            public object Service { get; set; }
            public int UserCount { get; set; }
        }

        // 记录所有正在使用的 COM 口或 IP
        private static readonly Dictionary<string, ConnectionInfo> _pool = new Dictionary<string, ConnectionInfo>();

        // 排队锁，防止几个设备同时来要钥匙导致抢号
        private static readonly object _lockObj = new object();

        // 辅助方法：生成取件码 (如 "RTU_COM1" 或 "TCP_192.168.0.10:502")
        private static string GetKey(DeviceConfig config)
        {
            return config.CommType == CommunicationType.ModbusTCP
                ? $"TCP_{config.IpAddress}:{config.Port}"
                : $"RTU_{config.ComPort}";
        }

        /// <summary>
        /// 连接Com口 (获取服务并增加使用人数)强占有时用这个
        /// </summary>
        public static object GetService(DeviceConfig config)
        {
            lock (_lockObj)
            {
                string key = GetKey(config);

                if (!_pool.ContainsKey(key))
                {
                    // 第一次有人用，新建一个服务，人数记为 1
                    object newService = config.CommType == CommunicationType.ModbusTCP
                        ? new ModbusTCPService(config.IpAddress, config.Port,config.SlaveId)
                        : new ModbusRTUService();

                    _pool[key] = new ConnectionInfo { Service = newService, UserCount = 1 };
                }
                else
                {
                    // 已经有别人开了这个端口，直接人数 +1
                    _pool[key].UserCount++;
                }

                return _pool[key].Service;
            }
        }

        /// <summary>
        /// 只获取现有服务，不增加使用人数 (专给后台轮询使用)弱占有时用这个
        /// </summary>
        public static object PeekService(DeviceConfig config)
        {
            lock (_lockObj)
            {
                string key = GetKey(config);
                if (_pool.ContainsKey(key))
                {
                    return _pool[key].Service;
                }
                return null;
            }
        }

        /// <summary>
        /// 断开COM口
        /// </summary>
        public static async Task ReleaseServiceAsync(DeviceConfig config)
        {
            object serviceToClose = null;

            lock (_lockObj)
            {
                string key = GetKey(config);

                if (_pool.ContainsKey(key))
                {
                    // 人数减 1
                    _pool[key].UserCount--;

                    // 核心：如果这个 COM 口真的一个人都没有了，准备物理关门
                    if (_pool[key].UserCount <= 0)
                    {
                        serviceToClose = _pool[key].Service;
                        _pool.Remove(key); // 从账本中划掉
                    }
                }
            }

            // 离开锁的区域后，让底层真正去执行“物理关门”
            if (serviceToClose != null)
            {
                if (serviceToClose is ModbusTCPService tcp)
                    await tcp.DisconnectAsync();
                else if (serviceToClose is ModbusRTUService rtu)
                    await rtu.DisconnectAsync();
            }
        }

        
    }
}
