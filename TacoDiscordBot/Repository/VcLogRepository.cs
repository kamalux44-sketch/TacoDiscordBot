using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Repository;

// 軽量リポジトリ。実行時に Npgsql が利用可能であれば使用します.
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

        Logger.Info("VcLogRepository 作成");
    }

    /// <summary>
    /// テーブルの存在を確認し、存在しなければ作成します。
    /// 起動時に一度呼び出す用途を想定しています。
    /// </summary>
    public async Task EnsureTableExistsAsync()
    {
        const string sql =
            @"
CREATE TABLE IF NOT EXISTS vc_log_targets (
    guild_id BIGINT PRIMARY KEY,
    channel_id BIGINT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);";

        Logger.Info("VcLogRepository: テーブル存在確認開始");

        await _base.ExecuteNonQueryAsync(sql);
    }

    /// <summary>
    /// 全てのターゲット設定を読み込みます。
    /// </summary>
    public async Task<IDictionary<ulong, ulong>> LoadAllAsync()
    {
        Logger.Info("VcLogRepository: 全ターゲットを読み込み開始");

        var dict = new Dictionary<ulong, ulong>();

        const string sql = "SELECT guild_id, channel_id FROM vc_log_targets";

        await _base.UseConnectionAsync(async conn =>
        {
            dynamic cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            dynamic reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var guildId = reader.GetInt64(0);
                var channelId = reader.GetInt64(1);

                dict[(ulong)guildId] = (ulong)channelId;
            }

            await reader.DisposeAsync();
        });

        Logger.Info("VcLogRepository: 読み込み完了 件数={Count}", dict.Count);

        return dict;
    }

    /// <summary>
    /// ギルドのログ先を登録・更新します。
    /// </summary>
    public async Task SetTargetAsync(ulong guildId, ulong channelId)
    {
        Logger.Info(
            "VcLogRepository: 設定 guild={GuildId} channel={ChannelId}",
            guildId,
            channelId
        );

        var sql =
            $"INSERT INTO vc_log_targets(guild_id, channel_id) "
            + $"VALUES({(long)guildId}, {(long)channelId}) "
            + "ON CONFLICT (guild_id) "
            + "DO UPDATE SET channel_id = EXCLUDED.channel_id";

        await _base.ExecuteNonQueryAsync(sql);

        Logger.Info("VcLogRepository: 設定完了");
    }

    /// <summary>
    /// ギルドのログ先を削除します。
    /// </summary>
    public async Task RemoveTargetAsync(ulong guildId)
    {
        Logger.Info("VcLogRepository: 設定削除 guild={GuildId}", guildId);

        var sql = $"DELETE FROM vc_log_targets " + $"WHERE guild_id = {(long)guildId}";

        await _base.ExecuteNonQueryAsync(sql);

        Logger.Info("VcLogRepository: 設定削除完了");
    }

    /// <summary>
    /// 指定ギルドのログ先チャンネルIDを取得します。
    /// </summary>
    public async Task<ulong?> GetTargetAsync(ulong guildId)
    {
        // 指定ギルドの VC ログ先チャンネルを取得します。
        Logger.Info("VcLogRepository: 設定取得 guild={GuildId}", guildId);

        var sql =
            $"SELECT channel_id "
            + $"FROM vc_log_targets "
            + $"WHERE guild_id = {(long)guildId} "
            + "LIMIT 1";

        object obj = null;

        await _base.UseConnectionAsync(async conn =>
        {
            dynamic cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            obj = await cmd.ExecuteScalarAsync();
        });

        if (obj == null || obj == DBNull.Value)
        {
            Logger.Info("VcLogRepository: 設定なし");
            return null;
        }

        var channelId = (ulong)(long)obj;

        Logger.Info("VcLogRepository: チャンネル取得 channel={ChannelId}", channelId);

        return channelId;
    }
}
