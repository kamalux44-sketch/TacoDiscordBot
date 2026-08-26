using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace TacoDiscordBot.Repository;

public class VcRankingRepository
{
    private readonly BaseRepository _base;

    public VcRankingRepository(BaseRepository baseRepo)
    {
        _base = baseRepo ?? throw new ArgumentNullException(nameof(baseRepo));
        _base.Log("VcRankingRepository 作成");
    }

    public async Task EnsureTableExistsAsync()
    {
        var sql = @"CREATE TABLE IF NOT EXISTS vc_sessions (
            id BIGSERIAL PRIMARY KEY,
            guild_id BIGINT NOT NULL,
            user_id BIGINT NOT NULL,
            channel_id BIGINT NOT NULL,
            joined_at TIMESTAMPTZ NOT NULL,
            left_at TIMESTAMPTZ,
            duration_seconds BIGINT
        );

        CREATE INDEX IF NOT EXISTS idx_vc_sessions_guild_user ON vc_sessions(guild_id, user_id);
        ";

        _base.Log("Ensuring table vc_sessions exists");
        await _base.ExecuteNonQueryAsync(sql);
    }

    public async Task<long> CreateVcSessionAsync(ulong guildId, ulong userId, ulong channelId, DateTime joinedAtUtc)
    {
        _base.Log($"CreateVcSessionAsync: guild={guildId} user={userId} channel={channelId}");
        object obj = null;
        await _base.UseConnectionAsync(async conn =>
        {
            dynamic cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO vc_sessions (guild_id, user_id, channel_id, joined_at) VALUES(@g, @u, @c, @j) RETURNING id";
            cmd.Parameters.AddWithValue("@g", (long)guildId);
            cmd.Parameters.AddWithValue("@u", (long)userId);
            cmd.Parameters.AddWithValue("@c", (long)channelId);
            cmd.Parameters.AddWithValue("@j", joinedAtUtc);
            obj = await cmd.ExecuteScalarAsync();
        });

        var id = (long)obj;
        _base.Log($"CreateVcSessionAsync: 作成 id={id}");
        return id;
    }

    public async Task CloseVcSessionAsync(long id, DateTime leftAtUtc, long durationSeconds)
    {
        _base.Log($"CloseVcSessionAsync: id={id} leftAt={leftAtUtc} duration={durationSeconds}");
        await _base.UseConnectionAsync(async conn =>
        {
            dynamic cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE vc_sessions SET left_at=@l, duration_seconds=@d WHERE id=@id";
            cmd.Parameters.AddWithValue("@l", leftAtUtc);
            cmd.Parameters.AddWithValue("@d", durationSeconds);
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        });
        _base.Log("CloseVcSessionAsync: 更新完了");
    }

    public async Task<bool> CloseLatestSessionForUserAsync(ulong guildId, ulong userId, DateTime leftAtUtc)
    {
        _base.Log($"CloseLatestSessionForUserAsync: guild={guildId} user={userId}");
        long id = -1;
        DateTime joined = DateTime.MinValue;

        await _base.UseConnectionAsync(async conn =>
        {
            dynamic cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT id, joined_at FROM vc_sessions WHERE guild_id = @g AND user_id = @u AND left_at IS NULL ORDER BY joined_at DESC LIMIT 1";
            cmd.Parameters.AddWithValue("@g", (long)guildId);
            cmd.Parameters.AddWithValue("@u", (long)userId);
            dynamic reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                await reader.DisposeAsync();
                return;
            }

            id = reader.GetInt64(0);
            joined = reader.GetDateTime(1);
            await reader.DisposeAsync();

            var duration = (long)(leftAtUtc - joined).TotalSeconds;
            dynamic ucmd = conn.CreateCommand();
            ucmd.CommandText = "UPDATE vc_sessions SET left_at=@l, duration_seconds=@d WHERE id=@id";
            ucmd.Parameters.AddWithValue("@l", leftAtUtc);
            ucmd.Parameters.AddWithValue("@d", duration);
            ucmd.Parameters.AddWithValue("@id", id);
            await ucmd.ExecuteNonQueryAsync();
        });

        _base.Log($"CloseLatestSessionForUserAsync: 終了 id={id}");
        return id != -1;
    }

    public async Task<System.Collections.Generic.List<(ulong userId, long totalSeconds)>> GetRankingAsync(ulong guildId, DateTime? sinceUtc)
    {
        var list = new System.Collections.Generic.List<(ulong, long)>();
        _base.Log($"GetRankingAsync: guild={guildId} since={(sinceUtc.HasValue ? sinceUtc.Value.ToString("o") : "all")}");
        await _base.UseConnectionAsync(async conn =>
        {
            dynamic cmd = conn.CreateCommand();
            if (sinceUtc.HasValue)
            {
                cmd.CommandText = @"SELECT user_id, SUM(duration_seconds) as total FROM vc_sessions WHERE guild_id = @g AND duration_seconds IS NOT NULL AND joined_at >= @since GROUP BY user_id ORDER BY total DESC LIMIT 50";
                cmd.Parameters.AddWithValue("@g", (long)guildId);
                cmd.Parameters.AddWithValue("@since", sinceUtc.Value);
            }
            else
            {
                cmd.CommandText = @"SELECT user_id, SUM(duration_seconds) as total FROM vc_sessions WHERE guild_id = @g AND duration_seconds IS NOT NULL GROUP BY user_id ORDER BY total DESC LIMIT 50";
                cmd.Parameters.AddWithValue("@g", (long)guildId);
            }

            dynamic reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var uid = (ulong)reader.GetInt64(0);
                var total = reader.IsDBNull(1) ? 0L : reader.GetInt64(1);
                list.Add((uid, total));
            }

            await reader.DisposeAsync();
        });

        _base.Log($"GetRankingAsync: 結果件数={list.Count}");
        return list;
    }
}

