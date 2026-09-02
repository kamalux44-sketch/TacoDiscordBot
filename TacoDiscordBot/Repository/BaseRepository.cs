using System;
using System.Threading.Tasks;

namespace TacoDiscordBot.Repository
{
    // 共通の DB 接続処理を提供するベースクラス
    public class BaseRepository
    {
        private readonly string _connString;

        public BaseRepository(string connString)
        {
            _connString = connString ?? throw new ArgumentNullException(nameof(connString));
        }

        private static Type GetConnectionType()
        {
            return Type.GetType("Npgsql.NpgsqlConnection, Npgsql");
        }

        /// <summary>
        /// 実行時に Npgsql プロバイダーが利用可能か確認します。
        /// </summary>
        public bool IsProviderAvailable()
        {
            return GetConnectionType() != null;
        }

        /// <summary>
        /// DB 接続を開き、処理完了後に必ず接続を解放します。
        /// </summary>
        public async Task<T> UseConnectionAsync<T>(Func<dynamic, Task<T>> func)
        {
            var connectionType = GetConnectionType();

            if (connectionType == null)
                throw new InvalidOperationException("Npgsql not available");

            dynamic conn = Activator.CreateInstance(connectionType, _connString);

            await conn.OpenAsync();

            try
            {
                return await func(conn);
            }
            finally
            {
                await conn.CloseAsync();
                await conn.DisposeAsync();
            }
        }

        public async Task UseConnectionAsync(Func<dynamic, Task> func)
        {
            await UseConnectionAsync<object>(async conn =>
            {
                await func(conn);
                return null;
            });
        }

        /// <summary>
        /// 結果を返さない SQL を実行します。
        /// </summary>
        public async Task ExecuteNonQueryAsync(string sql)
        {
            await UseConnectionAsync(async conn =>
            {
                dynamic cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                await cmd.ExecuteNonQueryAsync();
            });
        }
    }
}
