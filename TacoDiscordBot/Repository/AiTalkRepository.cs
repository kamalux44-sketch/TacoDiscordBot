using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Repository;

/// <summary>
/// AI 会話対象チャンネルの永続化リポジトリ。
/// VcLogRepository と同様の軽量実装。
/// </summary>
public class AiTalkRepository
{
    // AI 会話対象チャンネルの永続化を担当します。
    private readonly BaseRepository _base;

    public AiTalkRepository(BaseRepository baseRepo)
    {
        _base = baseRepo ?? throw new ArgumentNullException(nameof(baseRepo));

        Logger.Info("AiTalkRepository 作成");
    }

    public async Task EnsureTableExistsAsync()
    {
        // テーブルが存在しない場合、AI 会話対象チャンネル用のテーブルを作成します。
        const string sql = """
            CREATE TABLE IF NOT EXISTS ai_talk_targets (
                guild_id BIGINT PRIMARY KEY,
                channel_id BIGINT NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """;

        Logger.Info("AiTalkRepository: テーブル存在確認開始");

        await _base.ExecuteNonQueryAsync(sql);
    }

    public async Task<IDictionary<ulong, ulong>> LoadAllAsync()
    {
        // 保存済みのギルド・チャンネル設定をメモリ上の辞書へ読み込みます。
        Logger.Info("AiTalkRepository: 全ターゲットを読み込み開始");

        var dict = new Dictionary<ulong, ulong>();

        const string sql = "SELECT guild_id, channel_id FROM ai_talk_targets";

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

        Logger.Info("AiTalkRepository: 読み込み完了 件数={Count}", dict.Count);

        return dict;
    }

    public async Task SetTargetAsync(ulong guildId, ulong channelId)
    {
        // ギルドの AI 対象チャンネルを登録または更新します。
        Logger.Info(
            "AiTalkRepository: 設定 guild={GuildId} channel={ChannelId}",
            guildId,
            channelId
        );

        var sql =
            $"INSERT INTO ai_talk_targets(guild_id, channel_id) "
            + $"VALUES({(long)guildId}, {(long)channelId}) "
            + "ON CONFLICT (guild_id) "
            + "DO UPDATE SET channel_id = EXCLUDED.channel_id";

        await _base.ExecuteNonQueryAsync(sql);

        Logger.Info("AiTalkRepository: 設定完了");
    }

    public async Task<ulong?> GetTargetAsync(ulong guildId)
    {
        // 指定ギルドの AI 対象チャンネルを取得します。
        Logger.Info("AiTalkRepository: 設定取得 guild={GuildId}", guildId);

        var sql =
            $"SELECT channel_id "
            + $"FROM ai_talk_targets "
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
            Logger.Info("AiTalkRepository: 設定なし");
            return null;
        }

        var channelId = (ulong)(long)obj;

        Logger.Info("AiTalkRepository: チャンネル取得 channel={ChannelId}", channelId);

        return channelId;
    }

    public async Task RemoveTargetAsync(ulong guildId)
    {
        // 指定ギルドの AI 対象チャンネル設定を削除します。
        Logger.Info("AiTalkRepository: 設定削除 guild={GuildId}", guildId);

        var sql = $"DELETE FROM ai_talk_targets " + $"WHERE guild_id = {(long)guildId}";

        await _base.ExecuteNonQueryAsync(sql);

        Logger.Info("AiTalkRepository: 設定削除完了");
    }
}
