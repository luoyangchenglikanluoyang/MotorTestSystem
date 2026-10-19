using Dapper;
using MySqlConnector;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Thinger.DatabaseOperationlibrary
{
    /// <summary>
    /// 针对 MySQL 的具体数据库服务实现
    /// </summary>
    public class MySqlDatabaseService:IDatabaseService
    {
        // 这是一个私有只读字段，用来保存数据库的连接字符串
        // (包含IP地址、端口、用户名、密码等)

        private readonly string _connectionString;
        
        private readonly ILogger _logger;// 声明一个私有的日志字段

        /// <summary>
        /// 构造函数。当 Prism 创建这个服务时，必须传入连接字符串。
        /// 这样设计的好处是，连接字符串不会写死在代码里，可以从配置文件灵活读取。
        /// </summary>
        public MySqlDatabaseService(string connectionString, ILogger logger)
        {
            _logger = logger;
            _connectionString = connectionString;
        }

        /// <summary>
        /// 私有辅助方法：每次需要操作数据库时，获取一个全新的数据库连接。
        /// 很多新手会试图用一个全局的长连接，这在 C# 中是绝对错误的做法。
        /// 正确做法：用完即毁。底层的“连接池”会自动帮你管理物理连接，性能极高。
        /// </summary>
        private IDbConnection CreateConnection()
        {
            return new MySqlConnection(_connectionString);
        }

        public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object parameters = null)
        {
            // using 语法糖：无论代码是否发生异常，在离开花括号时，
            // 都会自动调用 connection.Dispose() 释放资源，绝不漏水。
            using (var connection = CreateConnection())
            {
                // 这里调用了 Dapper 提供的扩展方法 QueryAsync。
                // 只要你的 SQL 查询结果的列名，和你传入的 T (类) 的属性名一致，
                // Dapper 就会自动把数据转换成对象集合！这就是 ORM 的魔力。
                return await connection.QueryAsync<T>(sql, parameters);
            }
        }

        public async Task<T> QueryFirstOrDefaultAsync<T>(string sql, object parameters = null)
        {
            using (var connection = CreateConnection())
            {
                // 查询单条数据。如果没有找到，会返回该类型的默认值 (如果是类，就是 null)
                return await connection.QueryFirstOrDefaultAsync<T>(sql, parameters);
            }
        }

        public async Task<int> ExecuteAsync(string sql, object parameters = null)
        {
            using (var connection = CreateConnection())
            {
                // 执行 INSERT/UPDATE/DELETE。返回被影响的行数。
                return await connection.ExecuteAsync(sql, parameters);
            }
        }

        /// <summary>
        /// 数据库自检连接方法
        /// </summary>
        /// <returns></returns>
        public async Task<bool> TestConnectionAsync()
        {
            using (var connection = CreateConnection())
            {
                // 尝试打开连接。如果网络不通或密码错误，这里会自动抛出异常到调用的上一层。
                await ((MySqlConnection)connection).OpenAsync();
                return true;
            }
        }


    }
}
