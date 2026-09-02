using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Repository;

public class VcRankingRepository
{
    private readonly BaseRepository _base;

    public VcRankingRepository(BaseRepository baseRepo)
    {
        _base = baseRepo ?? throw new ArgumentNullException(nameof(baseRepo));

        Logger.Info("VcRankingRepository 作成");
    }

    public async Task EnsureTableExistsAsync()
    {
        // VC セッションとランキング集計に必要なテーブルを初期化します。
        var sql =
            @"
CREATE TABLE IF NOT EXISTS vc_sessions (
    id BIGSERIAL PRIMARY KEY,
    guild_id BIGINT NOT NULL,
    user_id BIGINT NOT NULL,
    channel_id BIGINT NOT NULL,
    joined_at TIMESTAMPTZ NOT NULL,
    left_at TIMESTAMPTZ,
    duration_seconds BIGINT
);

CREATE INDEX IF NOT EXISTS idx_vc_sessions_guild_user
    ON vc_sessions(guild_id, user_id);";

        Logger.Info("VcRankingRepository: テーブル存在確認開始");

        await _base.ExecuteNonQueryAsync(sql);
    }

    public async Task<long> CreateVcSessionAsync(
        ulong guildId,
        ulong userId,
        ulong channelId,
        DateTime joinedAtUtc
    )
    {
        // VC 入室時の開いたセッションを作成し、採番された ID を返します。
        Logger.Info(
            "VcRankingRepository: セッション作成 guild={GuildId} user={UserId} channel={ChannelId}",
            guildId,
            userId,
            channelId
        );

        object obj = null;

        await _base.UseConnectionAsync(async conn =>
        {
            dynamic cmd = conn.CreateCommand();

            cmd.CommandText =
                @"
INSERT INTO vc_sessions
(
    guild_id,
    user_id,
    channel_id,
    joined_at
)
VALUES
(
    @g,
    @u,
    @c,
    @j
)
RETURNING id;";

            cmd.Parameters.AddWithValue("@g", (long)guildId);
            cmd.Parameters.AddWithValue("@u", (long)userId);
            cmd.Parameters.AddWithValue("@c", (long)channelId);
            cmd.Parameters.AddWithValue("@j", joinedAtUtc);

            obj = await cmd.ExecuteScalarAsync();
        });

        var id = (long)obj;

        Logger.Info("VcRankingRepository: セッション作成完了 id={SessionId}", id);

        return id;
    }

    public async Task CloseVcSessionAsync(long id, DateTime leftAtUtc, long durationSeconds)
    {
        // 開いている VC セッションに退室時刻と滞在時間を記録します。
        Logger.Info(
            "VcRankingRepository: セッション終了 id={SessionId} leftAt={LeftAtUtc:o} duration={DurationSeconds}",
            id,
            leftAtUtc,
            durationSeconds
        );

        await _base.UseConnectionAsync(async conn =>
        {
            dynamic cmd = conn.CreateCommand();

            cmd.CommandText =
                @"
UPDATE vc_sessions
SET
    left_at = @l,
    duration_seconds = @d
WHERE id = @id;";

            cmd.Parameters.AddWithValue("@l", leftAtUtc);
            cmd.Parameters.AddWithValue("@d", durationSeconds);
            cmd.Parameters.AddWithValue("@id", id);

            await cmd.ExecuteNonQueryAsync();
        });

        Logger.Info("VcRankingRepository: セッション終了完了");
    }

    public async Task<bool> CloseLatestSessionForUserAsync(
        ulong guildId,
        ulong userId,
        DateTime leftAtUtc
    )
    {
        // メモリ上のセッションが失われた場合に、DB の最新セッションを終了します。
        Logger.Info(
            "VcRankingRepository: 最新セッション終了 guild={GuildId} user={UserId}",
            guildId,
            userId
        );

        long id = -1;
        DateTime joined = DateTime.MinValue;

        await _base.UseConnectionAsync(async conn =>
        {
            dynamic cmd = conn.CreateCommand();

            cmd.CommandText =
                @"
SELECT
    id,
    joined_at
FROM vc_sessions
WHERE guild_id = @g
  AND user_id = @u
  AND left_at IS NULL
ORDER BY joined_at DESC
LIMIT 1;";

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

            dynamic updateCommand = conn.CreateCommand();

            updateCommand.CommandText =
                @"
UPDATE vc_sessions
SET
    left_at = @l,
    duration_seconds = @d
WHERE id = @id;";

            updateCommand.Parameters.AddWithValue("@l", leftAtUtc);
            updateCommand.Parameters.AddWithValue("@d", duration);
            updateCommand.Parameters.AddWithValue("@id", id);

            await updateCommand.ExecuteNonQueryAsync();
        });

        Logger.Info("VcRankingRepository: 最新セッション終了完了 id={SessionId}", id);

        return id != -1;
    }

    public async Task<List<(ulong userId, long totalSeconds)>> GetRankingAsync(
        ulong guildId,
        DateTime? sinceUtc
    )
    {
        // 指定期間の滞在時間をユーザー単位で集計し、降順で取得します。
        var list = new List<(ulong userId, long totalSeconds)>();

        Logger.Info(
            "VcRankingRepository: ランキング取得 guild={GuildId} since={Since}",
            guildId,
            sinceUtc?.ToString("o") ?? "all"
        );

        await _base.UseConnectionAsync(async conn =>
        {
            dynamic cmd = conn.CreateCommand();

            if (sinceUtc.HasValue)
            {
                cmd.CommandText =
                    @"
SELECT
    user_id,
    SUM(duration_seconds) AS total
FROM vc_sessions
WHERE guild_id = @g
  AND duration_seconds IS NOT NULL
  AND joined_at >= @since
GROUP BY user_id
ORDER BY total DESC
LIMIT 50;";

                cmd.Parameters.AddWithValue("@g", (long)guildId);
                cmd.Parameters.AddWithValue("@since", sinceUtc.Value);
            }
            else
            {
                cmd.CommandText =
                    @"
SELECT
    user_id,
    SUM(duration_seconds) AS total
FROM vc_sessions
WHERE guild_id = @g
  AND duration_seconds IS NOT NULL
GROUP BY user_id
ORDER BY total DESC
LIMIT 50;";

                cmd.Parameters.AddWithValue("@g", (long)guildId);
            }

            dynamic reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var userId = (ulong)reader.GetInt64(0);
                var totalSeconds = reader.IsDBNull(1) ? 0L : reader.GetInt64(1);

                list.Add((userId, totalSeconds));
            }

            await reader.DisposeAsync();
        });

        Logger.Info("VcRankingRepository: ランキング取得完了 件数={Count}", list.Count);

        return list;
    }
}
