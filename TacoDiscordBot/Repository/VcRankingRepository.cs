using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace TacoDiscordBot.Repository;

public class VcRankingRepository
{
    private readonly string _connString;

    private VcRankingRepository(string connString)
    {
        _connString = connString;
    }

    public static VcRankingRepository TryCreateFromEnv()
    {
        try
        {
            var host = Environment.GetEnvironmentVariable("PGHOST");
            if (string.IsNullOrWhiteSpace(host))
                return null;

            var port = Environment.GetEnvironmentVariable("PGPORT") ?? "5432";
            var db = Environment.GetEnvironmentVariable("PGDATABASE") ?? "postgres";
            var user = Environment.GetEnvironmentVariable("PGUSER");
            var pass = Environment.GetEnvironmentVariable("PGPASSWORD");
            var ssl = Environment.GetEnvironmentVariable("PGSSLMODE");

            var parts = new List<string>
            {
                $"Host={host}",
                $"Port={port}",
                $"Database={db}"
            };
            if (!string.IsNullOrWhiteSpace(user)) parts.Add($"Username={user}");
            if (!string.IsNullOrWhiteSpace(pass)) parts.Add($"Password={pass}");
            if (!string.IsNullOrWhiteSpace(ssl)) parts.Add($"SslMode={ssl}");

            var conn = string.Join(";", parts);

            var t = Type.GetType("Npgsql.NpgsqlConnection, Npgsql");
            if (t == null) return null;

            var repo = new VcRankingRepository(conn);
            repo.EnsureTableExistsAsync().GetAwaiter().GetResult();
            return repo;
        }
        catch
        {
            return null;
        }
    }

    private async Task EnsureTableExistsAsync()
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

        await ExecuteNonQueryAsync(sql);
    }

    public async Task<long> CreateVcSessionAsync(ulong guildId, ulong userId, ulong channelId, DateTime joinedAtUtc)
    {
        dynamic conn = Activator.CreateInstance(Type.GetType("Npgsql.NpgsqlConnection, Npgsql"), _connString);
        await conn.OpenAsync();
        dynamic cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO vc_sessions (guild_id, user_id, channel_id, joined_at) VALUES(@g, @u, @c, @j) RETURNING id";
        cmd.Parameters.AddWithValue("@g", (long)guildId);
        cmd.Parameters.AddWithValue("@u", (long)userId);
        cmd.Parameters.AddWithValue("@c", (long)channelId);
        cmd.Parameters.AddWithValue("@j", joinedAtUtc);
        var obj = await cmd.ExecuteScalarAsync();
        long id = (long)obj;
        await conn.CloseAsync();
        await conn.DisposeAsync();
        return id;
    }

    public async Task CloseVcSessionAsync(long id, DateTime leftAtUtc, long durationSeconds)
    {
        dynamic conn = Activator.CreateInstance(Type.GetType("Npgsql.NpgsqlConnection, Npgsql"), _connString);
        await conn.OpenAsync();
        dynamic cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE vc_sessions SET left_at=@l, duration_seconds=@d WHERE id=@id";
        cmd.Parameters.AddWithValue("@l", leftAtUtc);
        cmd.Parameters.AddWithValue("@d", durationSeconds);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
        await conn.CloseAsync();
        await conn.DisposeAsync();
    }

    public async Task<bool> CloseLatestSessionForUserAsync(ulong guildId, ulong userId, DateTime leftAtUtc)
    {
        dynamic conn = Activator.CreateInstance(Type.GetType("Npgsql.NpgsqlConnection, Npgsql"), _connString);
        await conn.OpenAsync();
        dynamic cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, joined_at FROM vc_sessions WHERE guild_id = @g AND user_id = @u AND left_at IS NULL ORDER BY joined_at DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@g", (long)guildId);
        cmd.Parameters.AddWithValue("@u", (long)userId);
        dynamic reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            await reader.DisposeAsync();
            await conn.CloseAsync();
            await conn.DisposeAsync();
            return false;
        }

        long id = reader.GetInt64(0);
        DateTime joined = reader.GetDateTime(1);
        await reader.DisposeAsync();

        var duration = (long)(leftAtUtc - joined).TotalSeconds;
        dynamic ucmd = conn.CreateCommand();
        ucmd.CommandText = "UPDATE vc_sessions SET left_at=@l, duration_seconds=@d WHERE id=@id";
        ucmd.Parameters.AddWithValue("@l", leftAtUtc);
        ucmd.Parameters.AddWithValue("@d", duration);
        ucmd.Parameters.AddWithValue("@id", id);
        await ucmd.ExecuteNonQueryAsync();

        await conn.CloseAsync();
        await conn.DisposeAsync();
        return true;
    }

    public async Task<List<(ulong userId, long totalSeconds)>> GetRankingAsync(ulong guildId, DateTime? sinceUtc)
    {
        var list = new List<(ulong, long)>();
        dynamic conn = Activator.CreateInstance(Type.GetType("Npgsql.NpgsqlConnection, Npgsql"), _connString);
        await conn.OpenAsync();
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
        await conn.CloseAsync();
        await conn.DisposeAsync();
        return list;
    }

    private async Task ExecuteNonQueryAsync(string sql)
    {
        dynamic conn = Activator.CreateInstance(Type.GetType("Npgsql.NpgsqlConnection, Npgsql"), _connString);
        await conn.OpenAsync();
        dynamic cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
        await conn.CloseAsync();
        await conn.DisposeAsync();
    }
}
