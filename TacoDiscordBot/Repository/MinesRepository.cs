using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TacoDiscordBot.Models;

namespace TacoDiscordBot.Repository;

public sealed class MinesRepository : IMinesRepository
{
    private readonly BaseRepository _base;

    public MinesRepository(BaseRepository baseRepository)
    {
        _base = baseRepository ?? throw new ArgumentNullException(nameof(baseRepository));
    }

    public Task EnsureTablesExistAsync()
    {
        return _base.ExecuteNonQueryAsync("""
            CREATE TABLE IF NOT EXISTS mines_games (
                guild_id BIGINT NOT NULL,
                user_id BIGINT NOT NULL,
                bet BIGINT NOT NULL CHECK (bet > 0),
                bombs JSONB NOT NULL,
                opened JSONB NOT NULL,
                state VARCHAR(20) NOT NULL DEFAULT 'Playing',
                created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (guild_id, user_id)
            );
            """);
    }

    public async Task<bool> TryCreateAsync(MinesGame game)
    {
        object created = null;
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO mines_games(guild_id, user_id, bet, bombs, opened, state)
                VALUES (@guild_id, @user_id, @bet, @bombs, @opened, @state)
                ON CONFLICT (guild_id, user_id) DO NOTHING
                RETURNING user_id;
                """;
            command.Parameters.AddWithValue("@guild_id", (long)game.GuildId);
            command.Parameters.AddWithValue("@user_id", (long)game.UserId);
            command.Parameters.AddWithValue("@bet", game.Bet);
            command.Parameters.AddWithValue("@bombs", JsonSerializer.Serialize(game.Bombs.OrderBy(index => index)));
            command.Parameters.AddWithValue("@opened", JsonSerializer.Serialize(game.Opened));
            command.Parameters.AddWithValue("@state", game.State.ToString());
            created = await command.ExecuteScalarAsync();
        });

        return created != null && created != DBNull.Value;
    }

    public async Task<MinesGame> GetAsync(ulong guildId, ulong userId)
    {
        MinesGame result = null;
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                SELECT bet, bombs, opened, state
                FROM mines_games
                WHERE guild_id = @guild_id AND user_id = @user_id;
                """;
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            command.Parameters.AddWithValue("@user_id", (long)userId);
            dynamic reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var bombs = JsonSerializer.Deserialize<int[]>(reader.GetString(1)) ?? Array.Empty<int>();
                result = new MinesGame(guildId, userId, reader.GetInt64(0), bombs);
                foreach (var index in JsonSerializer.Deserialize<int[]>(reader.GetString(2)) ?? Array.Empty<int>())
                    result.Opened.Add(index);
                result.State = Enum.Parse<MinesGameState>(reader.GetString(3));
            }
            await reader.DisposeAsync();
        });

        return result;
    }

    public async Task SaveAsync(MinesGame game)
    {
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                UPDATE mines_games
                SET opened = @opened, state = @state, updated_at = now()
                WHERE guild_id = @guild_id AND user_id = @user_id;
                """;
            command.Parameters.AddWithValue("@guild_id", (long)game.GuildId);
            command.Parameters.AddWithValue("@user_id", (long)game.UserId);
            command.Parameters.AddWithValue("@opened", JsonSerializer.Serialize(game.Opened));
            command.Parameters.AddWithValue("@state", game.State.ToString());
            await command.ExecuteNonQueryAsync();
        });
    }

    public async Task DeleteAsync(ulong guildId, ulong userId)
    {
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM mines_games
                WHERE guild_id = @guild_id AND user_id = @user_id;
                """;
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            command.Parameters.AddWithValue("@user_id", (long)userId);
            await command.ExecuteNonQueryAsync();
        });
    }
}
