using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Thinger.DatabaseOperationlibrary
{
    /// <summary>
    /// 数据库服务接口。
    /// 定义了所有数据库操作的标准方法。未来的系统只要依赖这个接口即可。
    /// </summary>
    public interface IDatabaseService
    {
        /// <summary>
        /// 执行查询，返回一个集合 (例如：查询历史报警记录、电机测试数据)
        /// </summary>
        /// <typeparam name="T">你要返回的数据模型类型 (泛型)</typeparam>
        /// <param name="sql">SQL 语句</param>
        /// <param name="parameters">SQL 参数 (防止SQL注入用)</param>
        /// <returns>包含数据的列表</returns>
        Task<IEnumerable<T>> QueryAsync<T>(string sql, object parameters = null);

        /// <summary>
        /// 执行查询，返回单条数据 (例如：根据ID查询某一次具体的测试报告)
        /// </summary>
        Task<T> QueryFirstOrDefaultAsync<T>(string sql, object parameters = null);

        /// <summary>
        /// 执行增、删、改操作 (INSERT, UPDATE, DELETE)
        /// </summary>
        /// <returns>受影响的行数</returns>
        Task<int> ExecuteAsync(string sql, object parameters = null);

        /// <summary>
        /// 测试数据库是否能够成功连接 (常用于软件启动时的自检逻辑)
        /// </summary>
        Task<bool> TestConnectionAsync();
    }
}
