using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TacoDiscordBot.Models;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Repository;

// 軽量リポジトリ。実行時に Npgsql が利用可能であれば使用します。
public class BoRepository
{
    // 募集セッションと参加者の永続化を担当します。
    private readonly BaseRepository _base;
    public BoRepository(BaseRepository baseRepo)
    {
        _base = baseRepo ?? throw new ArgumentNullException(nameof(baseRepo));
    }

    public async Task EnsureTablesExistAsync()
    {
        // 募集本体と参加者を保存するテーブルを初期化します。
        var sql =
            @"
CREATE TABLE IF NOT EXISTS bo_sessions (
  session_id TEXT PRIMARY KEY,
  message_id BIGINT NOT NULL,
  channel_id BIGINT NOT NULL,
  body TEXT,
  at INTEGER NOT NULL,
  rank TEXT,
  deadline_raw TEXT,
  deadline_utc TIMESTAMPTZ,
  description TEXT,
  owner_id BIGINT NOT NULL,
  is_closed BOOLEAN NOT NULL DEFAULT FALSE,
  created_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS bo_participants (
  id BIGSERIAL PRIMARY KEY,
  session_id TEXT NOT NULL REFERENCES bo_sessions(session_id) ON DELETE CASCADE,
  user_id BIGINT NOT NULL,
  joined_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_bo_participants_session
    ON bo_participants(session_id);";

        Logger.Info("BoRepository: テーブル存在確認開始");

        await _base.ExecuteNonQueryAsync(sql);

        Logger.Info("BoRepository: テーブル存在確認完了");
    }

    public async Task CreateSessionAsync(BoSession session)
    {
        // 募集本体を保存した後、参加者を同じ接続で登録します。
        var sql =
            @"
INSERT INTO bo_sessions
(
    session_id,
    message_id,
    channel_id,
    body,
    at,
    rank,
    deadline_raw,
    deadline_utc,
    description,
    owner_id,
    is_closed,
    created_at
)
VALUES
(
    @sid,
    @mid,
    @cid,
    @body,
    @at,
    @rank,
    @draw,
    @dutc,
    @desc,
    @oid,
    @closed,
    @created
);";

        Logger.Info(
            "BoRepository: 募集作成 session={SessionId} owner={OwnerId}",
            session.SessionId,
            session.OwnerId
        );

        await _base.UseConnectionAsync(async conn =>
        {
            dynamic cmd = conn.CreateCommand();

            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@sid", session.SessionId);
            cmd.Parameters.AddWithValue("@mid", (long)session.MessageId);
            cmd.Parameters.AddWithValue("@cid", (long)session.ChannelId);
            cmd.Parameters.AddWithValue("@body", session.Body ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@at", session.At);
            cmd.Parameters.AddWithValue("@rank", session.Rank ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@draw", session.DeadlineRaw ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue(
                "@dutc",
                session.Deadline.HasValue ? (object)session.Deadline.Value : DBNull.Value
            );
            cmd.Parameters.AddWithValue("@desc", session.Description ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@oid", (long)session.OwnerId);
            cmd.Parameters.AddWithValue("@closed", session.IsClosed);
            cmd.Parameters.AddWithValue("@created", session.CreatedAt);

            await cmd.ExecuteNonQueryAsync();

            foreach (var uid in session.Participants)
            {
                const string participantSql =
                    @"
INSERT INTO bo_participants
(
    session_id,
    user_id,
    joined_at
)
VALUES
(
    @sid,
    @uid,
    @joined
);";

                dynamic participantCommand = conn.CreateCommand();

                participantCommand.CommandText = participantSql;
                participantCommand.Parameters.AddWithValue("@sid", session.SessionId);
                participantCommand.Parameters.AddWithValue("@uid", (long)uid);
                participantCommand.Parameters.AddWithValue("@joined", DateTime.UtcNow);

                await participantCommand.ExecuteNonQueryAsync();
            }
        });

        Logger.Info("BoRepository: 募集作成完了");
    }

    public async Task UpdateSessionAsync(BoSession session)
    {
        // 募集情報を更新し、参加者一覧を最新状態へ置き換えます。
        var sql =
            @"
UPDATE bo_sessions
SET
    message_id = @mid,
    channel_id = @cid,
    body = @body,
    at = @at,
    rank = @rank,
    deadline_raw = @draw,
    deadline_utc = @dutc,
    description = @desc,
    owner_id = @oid,
    is_closed = @closed,
    created_at = @created
WHERE session_id = @sid;";

        Logger.Info("BoRepository: 募集更新 session={SessionId}", session.SessionId);

        await _base.UseConnectionAsync(async conn =>
        {
            dynamic cmd = conn.CreateCommand();

            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@mid", (long)session.MessageId);
            cmd.Parameters.AddWithValue("@cid", (long)session.ChannelId);
            cmd.Parameters.AddWithValue("@body", session.Body ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@at", session.At);
            cmd.Parameters.AddWithValue("@rank", session.Rank ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@draw", session.DeadlineRaw ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue(
                "@dutc",
                session.Deadline.HasValue ? (object)session.Deadline.Value : DBNull.Value
            );
            cmd.Parameters.AddWithValue("@desc", session.Description ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@oid", (long)session.OwnerId);
            cmd.Parameters.AddWithValue("@closed", session.IsClosed);
            cmd.Parameters.AddWithValue("@created", session.CreatedAt);
            cmd.Parameters.AddWithValue("@sid", session.SessionId);

            await cmd.ExecuteNonQueryAsync();

            const string deleteSql = "DELETE FROM bo_participants WHERE session_id = @sid";

            dynamic deleteCommand = conn.CreateCommand();

            deleteCommand.CommandText = deleteSql;
            deleteCommand.Parameters.AddWithValue("@sid", session.SessionId);

            await deleteCommand.ExecuteNonQueryAsync();

            const string participantSql =
                @"
INSERT INTO bo_participants
(
    session_id,
    user_id,
    joined_at
)
VALUES
(
    @sid,
    @uid,
    @joined
);";

            foreach (var uid in session.Participants)
            {
                dynamic participantCommand = conn.CreateCommand();

                participantCommand.CommandText = participantSql;
                participantCommand.Parameters.AddWithValue("@sid", session.SessionId);
                participantCommand.Parameters.AddWithValue("@uid", (long)uid);
                participantCommand.Parameters.AddWithValue("@joined", DateTime.UtcNow);

                await participantCommand.ExecuteNonQueryAsync();
            }
        });

        Logger.Info("BoRepository: 募集更新完了");
    }

    public async Task CloseSessionAsync(string sessionId)
    {
        Logger.Info("BoRepository: 募集終了 session={SessionId}", sessionId);

        const string sql =
            "UPDATE bo_sessions SET is_closed = TRUE WHERE session_id = @sid";

        await _base.UseConnectionAsync(async conn =>
        {
            dynamic cmd = conn.CreateCommand();

            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@sid", sessionId);

            await cmd.ExecuteNonQueryAsync();
        });

        Logger.Info("BoRepository: 募集終了完了");
    }

    public async Task<List<BoSession>> LoadActiveSessionsAsync()
    {
        var list = new List<BoSession>();

        var sql =
            @"
SELECT
    s.session_id,
    s.message_id,
    s.channel_id,
    s.body,
    s.at,
    s.rank,
    s.deadline_raw,
    s.deadline_utc,
    s.description,
    s.owner_id,
    s.is_closed,
    s.created_at,
    COALESCE(
        array_agg(bp.user_id ORDER BY bp.id)
        FILTER (WHERE bp.user_id IS NOT NULL),
        ARRAY[]::bigint[]
    ) AS participants
FROM bo_sessions s
LEFT JOIN bo_participants bp
    ON bp.session_id = s.session_id
WHERE s.is_closed = false
GROUP BY
    s.session_id,
    s.message_id,
    s.channel_id,
    s.body,
    s.at,
    s.rank,
    s.deadline_raw,
    s.deadline_utc,
    s.description,
    s.owner_id,
    s.is_closed,
    s.created_at;";

        Logger.Info("BoRepository: 有効な募集を読み込み開始");

        await _base.UseConnectionAsync(async conn =>
        {
            dynamic cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            dynamic reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var session = new BoSession
                {
                    SessionId = reader.GetString(0),
                    MessageId = (ulong)reader.GetInt64(1),
                    ChannelId = (ulong)reader.GetInt64(2),
                    Body = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    At = reader.GetInt32(4),
                    Rank = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    DeadlineRaw = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    Deadline = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    Description = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    OwnerId = (ulong)reader.GetInt64(9),
                    IsClosed = reader.GetBoolean(10),
                    CreatedAt = reader.GetDateTime(11),
                    Participants = new List<ulong>(),
                };

                if (!reader.IsDBNull(12))
                {
                    var raw = reader.GetValue(12);

                    if (raw is long[] participants)
                    {
                        foreach (var userId in participants)
                        {
                            session.Participants.Add((ulong)userId);
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Unexpected participants type: {raw.GetType().FullName}"
                        );
                    }
                }

                list.Add(session);
            }

            await reader.DisposeAsync();
        });

        Logger.Info("BoRepository: 有効な募集を読み込み完了 件数={Count}", list.Count);

        return list;
    }
}
