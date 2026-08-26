using System;
using System.Reflection;
using System.Threading.Tasks;

namespace TacoDiscordBot.Repository
{
    // 共通の接続処理を提供する軽量なベースクラス
    public class BaseRepository
    {
        private readonly string _connString;
        private readonly Action<string> _log;

        public BaseRepository(string connString, Action<string> log)
        {
            _connString = connString ?? throw new ArgumentNullException(nameof(connString));
            _log = log ?? (_ => { });
        }

        public void Log(string message) => _log?.Invoke(message);

        private static Type GetConnectionType()
        {
            try
            {
                return Type.GetType("Npgsql.NpgsqlConnection, Npgsql");
            }
            catch
            {
                return null;
            }
        }

        public bool IsProviderAvailable()
        {
            return GetConnectionType() != null;
        }

        public async Task<T> UseConnectionAsync<T>(Func<dynamic, Task<T>> func)
        {
            var t = GetConnectionType();
            if (t == null) throw new InvalidOperationException("Npgsql not available");

            dynamic conn = Activator.CreateInstance(t, _connString);
            try
            {
                Log("Opening DB connection");
                await conn.OpenAsync();
                var res = await func(conn);
                return res;
            }
            finally
            {
                try
                {
                    Log("Closing DB connection");
                    await conn.CloseAsync();
                }
                catch (Exception ex)
                {
                    Log($"Error closing connection: {ex}");
                }

                try
                {
                    await conn.DisposeAsync();
                }
                catch { }
            }
        }

        public async Task UseConnectionAsync(Func<dynamic, Task> func)
        {
            await UseConnectionAsync<object>(async conn => { await func(conn); return null; });
        }

        public async Task ExecuteNonQueryAsync(string sql)
        {
            Log($"ExecuteNonQuery: {sql?.Split('\n')[0]}...");
            await UseConnectionAsync(async conn =>
            {
                dynamic cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();
            });
        }
    }
}
