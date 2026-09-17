using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TacoDiscordBot.Models;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Repository;

public sealed class ServerEventRepository
{
    private readonly BaseRepository _base;

    public ServerEventRepository(BaseRepository baseRepository)
    {
        _base = baseRepository ?? throw new ArgumentNullException(nameof(baseRepository));
    }

    public async Task EnsureTablesExistAsync()
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS server_events (
                guild_id BIGINT NOT NULL,
                event_type INTEGER NOT NULL,
                activator_id BIGINT NOT NULL,
                started_at TIMESTAMPTZ NOT NULL,
                ends_at TIMESTAMPTZ NOT NULL,
                is_personal BOOLEAN NOT NULL DEFAULT FALSE,
                PRIMARY KEY (guild_id, activator_id, is_personal)
            );
            CREATE INDEX IF NOT EXISTS ix_server_events_active
                ON server_events (guild_id, ends_at)
                WHERE is_personal = FALSE;
            CREATE UNIQUE INDEX IF NOT EXISTS ux_server_events_guild_server
                ON server_events (guild_id)
                WHERE is_personal = FALSE;
            """;
        await _base.ExecuteNonQueryAsync(sql);
        Logger.Info("ServerEventRepository: イベントテーブル確認・作成完了");
    }

    public async Task<bool> TryStartWithPaymentAsync(ServerEvent serverEvent, long cost)
    {
        if (cost <= 0)
            throw new ArgumentOutOfRangeException(nameof(cost));

        return await _base.UseTransactionAsync(async (connection, transaction) =>
        {
            dynamic createUserCommand = connection.CreateCommand();
            createUserCommand.Transaction = transaction;
            createUserCommand.CommandText = """
                INSERT INTO user_data(guild_id, user_id)
                VALUES (@guild_id, @activator_id)
                ON CONFLICT (guild_id, user_id) DO NOTHING;
                """;
            createUserCommand.Parameters.AddWithValue("@guild_id", (long)serverEvent.GuildId);
            createUserCommand.Parameters.AddWithValue("@activator_id", (long)serverEvent.ActivatorId);
            await createUserCommand.ExecuteNonQueryAsync();

            dynamic command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                WITH deducted AS (
                    UPDATE user_data
                    SET coins = coins - @cost, updated_at = now()
                    WHERE guild_id = @guild_id
                      AND user_id = @activator_id
                      AND coins >= @cost
                    RETURNING guild_id
                )
                INSERT INTO server_events(
                    guild_id, event_type, activator_id, started_at, ends_at, is_personal)
                SELECT @guild_id, @event_type, @activator_id, @started_at, @ends_at, @is_personal
                FROM deducted
                ON CONFLICT (guild_id, activator_id, is_personal) DO UPDATE SET
                    event_type = EXCLUDED.event_type,
                    started_at = EXCLUDED.started_at,
                    ends_at = EXCLUDED.ends_at
                RETURNING guild_id;
                """;
            command.Parameters.AddWithValue("@guild_id", (long)serverEvent.GuildId);
            command.Parameters.AddWithValue("@event_type", (int)serverEvent.Type);
            command.Parameters.AddWithValue("@activator_id", (long)serverEvent.ActivatorId);
            command.Parameters.AddWithValue("@started_at", serverEvent.StartedAt);
            command.Parameters.AddWithValue("@ends_at", serverEvent.EndsAt);
            command.Parameters.AddWithValue("@is_personal", serverEvent.IsPersonal);
            command.Parameters.AddWithValue("@cost", cost);

            var value = await command.ExecuteScalarAsync();
            return value != null && value != DBNull.Value;
        });
    }

    public async Task<IReadOnlyList<ServerEvent>> GetActiveAsync()
    {
        var events = new List<ServerEvent>();
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                SELECT guild_id, event_type, activator_id, started_at, ends_at, is_personal
                FROM server_events
                WHERE is_personal = FALSE OR ends_at > now();
                """;
            dynamic reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var serverEvent = new ServerEvent(
                    (ulong)reader.GetInt64(0),
                    (EventType)reader.GetInt32(1),
                    (ulong)reader.GetInt64(2),
                    reader.GetFieldValue<DateTimeOffset>(3),
                    reader.GetFieldValue<DateTimeOffset>(4),
                    reader.GetBoolean(5));
                if (serverEvent.IsPersonal || serverEvent.IsActive(DateTimeOffset.UtcNow))
                    events.Add(serverEvent);
            }
            await reader.DisposeAsync();
        });
        return events;
    }

    public async Task SaveAsync(ServerEvent serverEvent)
    {
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO server_events(guild_id, event_type, activator_id, started_at, ends_at, is_personal)
                VALUES (@guild_id, @event_type, @activator_id, @started_at, @ends_at, @is_personal)
                ON CONFLICT (guild_id, activator_id, is_personal) DO UPDATE SET
                    event_type = EXCLUDED.event_type,
                    started_at = EXCLUDED.started_at,
                    ends_at = EXCLUDED.ends_at;
                """;
            command.Parameters.AddWithValue("@guild_id", (long)serverEvent.GuildId);
            command.Parameters.AddWithValue("@event_type", (int)serverEvent.Type);
            command.Parameters.AddWithValue("@activator_id", (long)serverEvent.ActivatorId);
            command.Parameters.AddWithValue("@started_at", serverEvent.StartedAt);
            command.Parameters.AddWithValue("@ends_at", serverEvent.EndsAt);
            command.Parameters.AddWithValue("@is_personal", serverEvent.IsPersonal);
            await command.ExecuteNonQueryAsync();
        });
    }

    public async Task DeleteAsync(ServerEvent serverEvent)
    {
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM server_events
                WHERE guild_id = @guild_id AND activator_id = @activator_id AND is_personal = @is_personal;
                """;
            command.Parameters.AddWithValue("@guild_id", (long)serverEvent.GuildId);
            command.Parameters.AddWithValue("@activator_id", (long)serverEvent.ActivatorId);
            command.Parameters.AddWithValue("@is_personal", serverEvent.IsPersonal);
            await command.ExecuteNonQueryAsync();
        });
    }
}
