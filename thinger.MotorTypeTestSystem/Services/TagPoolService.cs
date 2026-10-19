using HandyControl.Collections;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using thinger.MotorTypeTestSystem.Models;

namespace thinger.MotorTypeTestSystem.Services
{
    /// <summary>
    /// 全局变量池服务
    /// </summary>
    public class TagPoolService
    {
        // 核心：一个线程安全的字典，专门存所有的 TagItem,只负责数据存储和更新。
        private readonly ConcurrentDictionary<string, TagItem> _Datapool = new();

        // 索引器,这让前端能用 TagPool["变量名"] 直接绑定
        //微软规定：如果你想让你自己这个类（而不是类里面的某个属性），在
        //外面用起来长得像一个数组或字典一样（比如能在外面直接写 TagPool["电压"]），
        //你就必须在类里面写一段特殊的代码，也就是 public TagItem this[string tagName]。
        //这是专门给 WPF 界面提供的“智能查阅窗口”。
        //在前端 XAML 代码里写的 {Binding TagPool[IO.Voltage_U].Value}，就会直接调用这段代码。
        //this表示当前自己的这个类也就是TagPoolService
        public TagItem this[string tagName]
        {
            get
            {
                // 如果池子里没有这个名字，就自动建一个初始值为 0 的进去，防止程序崩溃
                if (!_Datapool.ContainsKey(tagName))
                {
                    _Datapool[tagName] = new TagItem(tagName, 0);
                }
                return _Datapool[tagName];
            }
        }

        // 后台写数据用的方法
        public void SetValue(string tagName, object value)
        {
            this[tagName].Value = value;
        }

        // 后台读数据用的方法
        public T GetValue<T>(string tagName)
        {
            var val = this[tagName].Value;
            if (val == null) return default;//default在这里是给你一个指定类型的默认值。
            return (T)val; // 强转成你需要的类型（比如 float 或 int）
        }

    }
}
