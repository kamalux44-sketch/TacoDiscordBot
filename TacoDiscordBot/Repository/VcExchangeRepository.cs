using Npgsql;
using TacoDiscordBot.Models;

namespace TacoDiscordBot.Repository;

public sealed class VcExchangeRepository
{
    private const long ExchangeUnitSeconds = 6 * 60;
    private const long CoinsPerUnit = 25;
    private readonly BaseRepository _base;

    public VcExchangeRepository(BaseRepository baseRepository)
    {
        _base = baseRepository ?? throw new ArgumentNullException(nameof(baseRepository));
    }

    public async Task EnsureTablesExistAsync()
    {
        await _base.ExecuteNonQueryAsync("""
            CREATE TABLE IF NOT EXISTS vc_exchange_balances (
                guild_id BIGINT NOT NULL,
                user_id BIGINT NOT NULL,
                exchanged_seconds BIGINT NOT NULL DEFAULT 0 CHECK (exchanged_seconds >= 0),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (guild_id, user_id)
            );
            """);
    }

    public async Task<VcExchangeSummary> GetSummaryAsync(ulong guildId, ulong userId)
    {
        VcExchangeSummary result = null;
        await _base.UseNpgsqlTransactionAsync(async (connection, transaction) =>
        {
            result = await ReadSummaryAsync(connection, transaction, guildId, userId, false, false);
            return result;
        });
        return result ?? new VcExchangeSummary(0, 0);
    }

    public async Task<VcExchangeResult> ExchangeAsync(ulong guildId, ulong userId)
    {
        return await _base.UseNpgsqlTransactionAsync(async (connection, transaction) =>
        {
            var summary = await ReadSummaryAsync(connection, transaction, guildId, userId, true, true);
            var exchangeUnits = summary.UnexchangedSeconds / ExchangeUnitSeconds;
            var exchangeMinutes = exchangeUnits * 6;
            var coins = exchangeUnits * CoinsPerUnit;

            if (exchangeUnits == 0)
                return new VcExchangeResult(false, 0, 0, summary.UnexchangedSeconds / 60);

            await EnsureUserDataAsync(connection, transaction, guildId, userId);
            await AddExchangedSecondsAsync(
                connection,
                transaction,
                guildId,
                userId,
                exchangeMinutes * 60
            );
            await AddCoinsAsync(connection, transaction, guildId, userId, coins);

            var remainingSeconds = summary.UnexchangedSeconds - exchangeMinutes * 60;
            return new VcExchangeResult(true, exchangeMinutes, coins, remainingSeconds / 60);
        });
    }

    private static async Task<VcExchangeSummary> ReadSummaryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ulong guildId,
        ulong userId,
        bool forUpdate,
        bool ensureBalance
    )
    {
        if (ensureBalance)
            await EnsureExchangeBalanceAsync(connection, transaction, guildId, userId);

        var sql = forUpdate
            ? """
                SELECT
                    COALESCE((
                        SELECT SUM(duration_seconds)
                        FROM vc_sessions
                        WHERE guild_id = @guild_id
                          AND user_id = @user_id
                          AND duration_seconds IS NOT NULL
                    ), 0),
                    COALESCE(exchanged_seconds, 0)
                FROM vc_exchange_balances
                WHERE guild_id = @guild_id AND user_id = @user_id
                FOR UPDATE;
                """
            : """
                SELECT
                    COALESCE((
                        SELECT SUM(duration_seconds)
                        FROM vc_sessions
                        WHERE guild_id = @guild_id
                          AND user_id = @user_id
                          AND duration_seconds IS NOT NULL
                    ), 0),
                    COALESCE((
                        SELECT exchanged_seconds
                        FROM vc_exchange_balances
                        WHERE guild_id = @guild_id AND user_id = @user_id
                    ), 0);
                """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("guild_id", (long)guildId);
        command.Parameters.AddWithValue("user_id", (long)userId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return new VcExchangeSummary(0, 0);

        return new VcExchangeSummary(reader.GetInt64(0), reader.GetInt64(1));
    }

    private static async Task EnsureExchangeBalanceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ulong guildId,
        ulong userId
    )
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO vc_exchange_balances(guild_id, user_id)
            VALUES (@guild_id, @user_id)
            ON CONFLICT (guild_id, user_id) DO NOTHING;
            """, connection, transaction);
        command.Parameters.AddWithValue("guild_id", (long)guildId);
        command.Parameters.AddWithValue("user_id", (long)userId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task EnsureUserDataAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ulong guildId,
        ulong userId
    )
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO user_data(guild_id, user_id)
            VALUES (@guild_id, @user_id)
            ON CONFLICT (guild_id, user_id) DO NOTHING;
            """, connection, transaction);
        command.Parameters.AddWithValue("guild_id", (long)guildId);
        command.Parameters.AddWithValue("user_id", (long)userId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AddExchangedSecondsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ulong guildId,
        ulong userId,
        long seconds
    )
    {
        await using var command = new NpgsqlCommand("""
            UPDATE vc_exchange_balances
            SET exchanged_seconds = exchanged_seconds + @seconds,
                updated_at = now()
            WHERE guild_id = @guild_id AND user_id = @user_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("seconds", seconds);
        command.Parameters.AddWithValue("guild_id", (long)guildId);
        command.Parameters.AddWithValue("user_id", (long)userId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AddCoinsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ulong guildId,
        ulong userId,
        long coins
    )
    {
        await using var command = new NpgsqlCommand("""
            UPDATE user_data
            SET coins = coins + @coins, updated_at = now()
            WHERE guild_id = @guild_id AND user_id = @user_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("coins", coins);
        command.Parameters.AddWithValue("guild_id", (long)guildId);
        command.Parameters.AddWithValue("user_id", (long)userId);
        await command.ExecuteNonQueryAsync();
    }
}
