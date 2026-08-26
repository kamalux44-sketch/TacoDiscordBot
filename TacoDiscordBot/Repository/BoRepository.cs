using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TacoDiscordBot.Models;

namespace TacoDiscordBot.Repository;

// 軽量リポジトリ。実行時に Npgsql が利用可能であれば使用します。
public class BoRepository
{
    private readonly BaseRepository _base;

    public BoRepository(BaseRepository baseRepo)
    {
        _base = baseRepo ?? throw new ArgumentNullException(nameof(baseRepo));
    }

    public async Task EnsureTablesExistAsync()
    {
        var sql = @"CREATE TABLE IF NOT EXISTS bo_sessions (
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

CREATE INDEX IF NOT EXISTS idx_bo_participants_session ON bo_participants(session_id);";

        // テーブルの存在を確認し、必要なら作成する
        _base.Log("EnsureTablesExistAsync: bo_sessions/bo_participants の存在確認開始");
        await _base.ExecuteNonQueryAsync(sql);
        _base.Log("EnsureTablesExistAsync: 完了");
    }

    public async Task CreateSessionAsync(BoSession session)
    {
        var sql = @"INSERT INTO bo_sessions (session_id, message_id, channel_id, body, at, rank, deadline_raw, deadline_utc, description, owner_id, is_closed, created_at)
VALUES (@sid, @mid, @cid, @body, @at, @rank, @draw, @dutc, @desc, @oid, @closed, @created);";

        _base.Log($"CreateSessionAsync: session={session.SessionId} owner={session.OwnerId}");
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
            cmd.Parameters.AddWithValue("@dutc", session.Deadline.HasValue ? (object)session.Deadline.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@desc", session.Description ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@oid", (long)session.OwnerId);
            cmd.Parameters.AddWithValue("@closed", session.IsClosed);
            cmd.Parameters.AddWithValue("@created", session.CreatedAt);

            await cmd.ExecuteNonQueryAsync();

            // 参加者を挿入
            foreach (var uid in session.Participants)
            {
                var psql = "INSERT INTO bo_participants(session_id, user_id, joined_at) VALUES(@sid, @uid, @joined)";
                dynamic pcmd = conn.CreateCommand();
                pcmd.CommandText = psql;
                pcmd.Parameters.AddWithValue("@sid", session.SessionId);
                pcmd.Parameters.AddWithValue("@uid", (long)uid);
                pcmd.Parameters.AddWithValue("@joined", DateTime.UtcNow);
                await pcmd.ExecuteNonQueryAsync();
            }
        });
        _base.Log("CreateSessionAsync: 完了");
    }

    public async Task UpdateSessionAsync(BoSession session)
    {
        var sql = @"UPDATE bo_sessions SET message_id=@mid, channel_id=@cid, body=@body, at=@at, rank=@rank, deadline_raw=@draw, deadline_utc=@dutc, description=@desc, owner_id=@oid, is_closed=@closed, created_at=@created WHERE session_id=@sid";

        _base.Log($"UpdateSessionAsync: session={session.SessionId}");
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
            cmd.Parameters.AddWithValue("@dutc", session.Deadline.HasValue ? (object)session.Deadline.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@desc", session.Description ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@oid", (long)session.OwnerId);
            cmd.Parameters.AddWithValue("@closed", session.IsClosed);
            cmd.Parameters.AddWithValue("@created", session.CreatedAt);
            cmd.Parameters.AddWithValue("@sid", session.SessionId);
            await cmd.ExecuteNonQueryAsync();

            // 参加者を置換: 既存を削除してから挿入
            var dsql = "DELETE FROM bo_participants WHERE session_id = @sid";
            dynamic dcmd = conn.CreateCommand();
            dcmd.CommandText = dsql;
            dcmd.Parameters.AddWithValue("@sid", session.SessionId);
            await dcmd.ExecuteNonQueryAsync();

            foreach (var uid in session.Participants)
            {
                var psql = "INSERT INTO bo_participants(session_id, user_id, joined_at) VALUES(@sid, @uid, @joined)";
                dynamic pcmd = conn.CreateCommand();
                pcmd.CommandText = psql;
                pcmd.Parameters.AddWithValue("@sid", session.SessionId);
                pcmd.Parameters.AddWithValue("@uid", (long)uid);
                pcmd.Parameters.AddWithValue("@joined", DateTime.UtcNow);
                await pcmd.ExecuteNonQueryAsync();
            }
        });
        _base.Log("UpdateSessionAsync: 完了");
    }
    public async Task DeleteSessionAsync(string sessionId)
    {
        _base.Log($"DeleteSessionAsync: session={sessionId}");
        var sql = "DELETE FROM bo_sessions WHERE session_id = @sid";
        await _base.UseConnectionAsync(async conn =>
        {
            dynamic cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@sid", sessionId);
            await cmd.ExecuteNonQueryAsync();
        });
        _base.Log("DeleteSessionAsync: 完了");
    }

    public async Task<System.Collections.Generic.List<BoSession>> LoadActiveSessionsAsync()
    {
        var list = new System.Collections.Generic.List<BoSession>();
        var sql = "SELECT session_id, message_id, channel_id, body, at, rank, deadline_raw, deadline_utc, description, owner_id, is_closed, created_at FROM bo_sessions WHERE is_closed = false";
        _base.Log("LoadActiveSessionsAsync: 読み込み開始");
        await _base.UseConnectionAsync(async conn =>
        {
            dynamic cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            dynamic reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var s = new BoSession();
                s.SessionId = reader.GetString(0);
                s.MessageId = (ulong)reader.GetInt64(1);
                s.ChannelId = (ulong)reader.GetInt64(2);
                s.Body = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                s.At = reader.GetInt32(4);
                s.Rank = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
                s.DeadlineRaw = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
                s.Deadline = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7);
                s.Description = reader.IsDBNull(8) ? string.Empty : reader.GetString(8);
                s.OwnerId = (ulong)reader.GetInt64(9);
                s.IsClosed = reader.GetBoolean(10);
                s.CreatedAt = reader.GetDateTime(11);

                // 参加者を読み込む
                s.Participants = new System.Collections.Generic.List<ulong>();
                var psql = "SELECT user_id FROM bo_participants WHERE session_id = @sid ORDER BY id";
                dynamic pcmd = conn.CreateCommand();
                pcmd.CommandText = psql;
                pcmd.Parameters.AddWithValue("@sid", s.SessionId);
                dynamic preader = await pcmd.ExecuteReaderAsync();
                while (await preader.ReadAsync())
                {
                    s.Participants.Add((ulong)preader.GetInt64(0));
                }
                await preader.DisposeAsync();

                list.Add(s);
            }
            await reader.DisposeAsync();
        });

        _base.Log($"LoadActiveSessionsAsync: 読み込み完了 件数={list.Count}");
        return list;
    }
}

