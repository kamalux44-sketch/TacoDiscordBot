using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace TacoDiscordBot.Repository;

// 軽量リポジトリ。実行時に Npgsql が利用可能であれば使用します。
/// <summary>
/// VC ログの永続化リポジトリ。
/// BaseRepository を介して DB 接続を行い、操作の開始・終了をログ出力します。
/// </summary>
public class VcLogRepository
{
    private readonly BaseRepository _base;

    public VcLogRepository(BaseRepository baseRepo)
    {
        _base = baseRepo ?? throw new ArgumentNullException(nameof(baseRepo));
        _base.Log("VcLogRepository 作成");
    }

    /// <summary>
    /// テーブルの存在を確認し、存在しなければ作成します。
    /// 起動時に一度呼び出す用途を想定しています。
    /// </summary>
    public async Task EnsureTableExistsAsync()
    {
        var sql = @"CREATE TABLE IF NOT EXISTS vc_log_targets (
            guild_id BIGINT PRIMARY KEY,
            channel_id BIGINT NOT NULL,
            updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
        );";

        _base.Log("Ensuring table vc_log_targets exists");
        await _base.ExecuteNonQueryAsync(sql);
    }

    /// <summary>
    /// 全てのターゲット設定を読み込みます。
    /// </summary>
    public async Task<System.Collections.Generic.IDictionary<ulong, ulong>> LoadAllAsync()
    {
        _base.Log("LoadAllAsync: 全ターゲットを読み込み開始");
        var dict = new System.Collections.Generic.Dictionary<ulong, ulong>();
        var sql = "SELECT guild_id, channel_id FROM vc_log_targets";

        await _base.UseConnectionAsync(async conn =>
        {
            dynamic cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            dynamic reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long g = reader.GetInt64(0);
                long c = reader.GetInt64(1);
                dict[(ulong)g] = (ulong)c;
            }
            await reader.DisposeAsync();
        });

        _base.Log($"LoadAllAsync: 読み込み完了 件数={dict.Count}");
        return dict;
    }

    /// <summary>
    /// ギルドのログ先を登録・更新します。
    /// </summary>
    public async Task SetTargetAsync(ulong guildId, ulong channelId)
    {
        _base.Log($"SetTargetAsync: guild={guildId} channel={channelId}");
        var sql = $"INSERT INTO vc_log_targets(guild_id, channel_id) VALUES({(long)guildId}, {(long)channelId}) ON CONFLICT (guild_id) DO UPDATE SET channel_id = EXCLUDED.channel_id";
        await _base.ExecuteNonQueryAsync(sql);
        _base.Log("SetTargetAsync: 完了");
    }

    /// <summary>
    /// ギルドのログ先を削除します。
    /// </summary>
    public async Task RemoveTargetAsync(ulong guildId)
    {
        _base.Log($"RemoveTargetAsync: guild={guildId}");
        var sql = $"DELETE FROM vc_log_targets WHERE guild_id = {(long)guildId}";
        await _base.ExecuteNonQueryAsync(sql);
        _base.Log("RemoveTargetAsync: 完了");
    }

    /// <summary>
    /// 指定ギルドのログ先チャンネルIDを取得します。
    /// </summary>
    public async Task<ulong?> GetTargetAsync(ulong guildId)
    {
        _base.Log($"GetTargetAsync: guild={guildId}");
        var sql = $"SELECT channel_id FROM vc_log_targets WHERE guild_id = {(long)guildId} LIMIT 1";
        object obj = null;
        await _base.UseConnectionAsync(async conn =>
        {
            dynamic cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            obj = await cmd.ExecuteScalarAsync();
        });

        if (obj == null || obj == DBNull.Value)
        {
            _base.Log("GetTargetAsync: 未設定");
            return null;
        }

        var val = (ulong)(long)obj;
        _base.Log($"GetTargetAsync: found channel={val}");
        return val;
    }
}

