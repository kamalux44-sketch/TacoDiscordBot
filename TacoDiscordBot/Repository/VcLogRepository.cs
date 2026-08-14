using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace TacoDiscordBot.Repository;

// Lightweight repository that uses Npgsql if available at runtime.
public class VcLogRepository
{
    private readonly string _connString;

    private VcLogRepository(string connString)
    {
        _connString = connString;
    }

    public static VcLogRepository TryCreateFromEnv()
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

            // Check if Npgsql assembly is available
            var asm = TryLoadNpgsqlAssembly();
            if (asm == null) return null;

            var repo = new VcLogRepository(conn);
            repo.EnsureTableExistsAsync().GetAwaiter().GetResult();
            return repo;
        }
        catch
        {
            return null;
        }
    }

    private static Assembly TryLoadNpgsqlAssembly()
    {
        try
        {
            // try common assembly name
            var t = Type.GetType("Npgsql.NpgsqlConnection, Npgsql");
            if (t != null) return t.Assembly;
            // fallback: try loading
            return Assembly.Load("Npgsql");
        }
        catch
        {
            return null;
        }
    }

    private async Task EnsureTableExistsAsync()
    {
        var sql = @"CREATE TABLE IF NOT EXISTS vc_log_targets (
            guild_id BIGINT PRIMARY KEY,
            channel_id BIGINT NOT NULL,
            updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
        );

        CREATE TABLE IF NOT EXISTS vc_sessions (
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
        var asm = TryLoadNpgsqlAssembly();
        if (asm == null) throw new InvalidOperationException("Npgsql not available");

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
        var asm = TryLoadNpgsqlAssembly();
        if (asm == null) throw new InvalidOperationException("Npgsql not available");

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

    // If in-memory open session id is missing, try to close the latest open session for the guild+user
    public async Task<bool> CloseLatestSessionForUserAsync(ulong guildId, ulong userId, DateTime leftAtUtc)
    {
        var asm = TryLoadNpgsqlAssembly();
        if (asm == null) return false;

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
        var asm = TryLoadNpgsqlAssembly();
        if (asm == null) return list;

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

    public async Task<IDictionary<ulong, ulong>> LoadAllAsync()
    {
        var dict = new Dictionary<ulong, ulong>();
        var sql = "SELECT guild_id, channel_id FROM vc_log_targets";
        var asm = TryLoadNpgsqlAssembly();
        if (asm == null) return dict;

        dynamic conn = Activator.CreateInstance(Type.GetType("Npgsql.NpgsqlConnection, Npgsql"), _connString);
        await conn.OpenAsync();
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
        await conn.CloseAsync();
        await conn.DisposeAsync();
        return dict;
    }

    public async Task SetTargetAsync(ulong guildId, ulong channelId)
    {
        var sql = $"INSERT INTO vc_log_targets(guild_id, channel_id) VALUES({(long)guildId}, {(long)channelId}) ON CONFLICT (guild_id) DO UPDATE SET channel_id = EXCLUDED.channel_id";
        await ExecuteNonQueryAsync(sql);
    }

    public async Task RemoveTargetAsync(ulong guildId)
    {
        var sql = $"DELETE FROM vc_log_targets WHERE guild_id = {(long)guildId}";
        await ExecuteNonQueryAsync(sql);
    }

    public async Task<ulong?> GetTargetAsync(ulong guildId)
    {
        var sql = $"SELECT channel_id FROM vc_log_targets WHERE guild_id = {(long)guildId} LIMIT 1";
        var asm = TryLoadNpgsqlAssembly();
        if (asm == null) return null;

        dynamic conn = Activator.CreateInstance(Type.GetType("Npgsql.NpgsqlConnection, Npgsql"), _connString);
        await conn.OpenAsync();
        dynamic cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var obj = await cmd.ExecuteScalarAsync();
        await conn.CloseAsync();
        await conn.DisposeAsync();
        if (obj == null || obj == DBNull.Value) return null;
        return (ulong)(long)obj;
    }

    private async Task ExecuteNonQueryAsync(string sql)
    {
        var asm = TryLoadNpgsqlAssembly();
        if (asm == null) throw new InvalidOperationException("Npgsql not available");

        dynamic conn = Activator.CreateInstance(Type.GetType("Npgsql.NpgsqlConnection, Npgsql"), _connString);
        await conn.OpenAsync();
        dynamic cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
        await conn.CloseAsync();
        await conn.DisposeAsync();
    }
}
