using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TacoDiscordBot.Repository;

/// <summary>
/// AI 会話対象チャンネルの永続化リポジトリ。
/// VcLogRepository と同様の軽量実装。
/// </summary>
public class AiTalkRepository
{
    private readonly BaseRepository _base;

    public AiTalkRepository(BaseRepository baseRepo)
    {
        _base = baseRepo ?? throw new ArgumentNullException(nameof(baseRepo));
        _base.Log("AiTalkRepository 作成");
    }

    public async Task EnsureTableExistsAsync()
    {
        var sql = @"CREATE TABLE IF NOT EXISTS ai_talk_targets (
            guild_id BIGINT PRIMARY KEY,
            channel_id BIGINT NOT NULL,
            updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
        );";

        _base.Log("Ensuring table ai_talk_targets exists");
        await _base.ExecuteNonQueryAsync(sql);
    }

    public async Task<IDictionary<ulong, ulong>> LoadAllAsync()
    {
        _base.Log("LoadAllAsync: 全ターゲットを読み込み開始");
        var dict = new Dictionary<ulong, ulong>();
        var sql = "SELECT guild_id, channel_id FROM ai_talk_targets";

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

    public async Task SetTargetAsync(ulong guildId, ulong channelId)
    {
        _base.Log($"SetTargetAsync: guild={guildId} channel={channelId}");
        var sql = $"INSERT INTO ai_talk_targets(guild_id, channel_id) VALUES({(long)guildId}, {(long)channelId}) ON CONFLICT (guild_id) DO UPDATE SET channel_id = EXCLUDED.channel_id";
        await _base.ExecuteNonQueryAsync(sql);
        _base.Log("SetTargetAsync: 完了");
    }

    public async Task<ulong?> GetTargetAsync(ulong guildId)
    {
        _base.Log($"GetTargetAsync: guild={guildId}");
        var sql = $"SELECT channel_id FROM ai_talk_targets WHERE guild_id = {(long)guildId} LIMIT 1";
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

    public async Task RemoveTargetAsync(ulong guildId)
    {
        _base.Log($"RemoveTargetAsync: guild={guildId}");
        var sql = $"DELETE FROM ai_talk_targets WHERE guild_id = {(long)guildId}";
        await _base.ExecuteNonQueryAsync(sql);
        _base.Log("RemoveTargetAsync: 完了");
    }
}
