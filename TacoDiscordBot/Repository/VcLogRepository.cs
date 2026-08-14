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
        );";

        await ExecuteNonQueryAsync(sql);
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

