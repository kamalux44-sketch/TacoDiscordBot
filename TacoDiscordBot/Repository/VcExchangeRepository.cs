using System;
using System.Threading.Tasks;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Repository;

public sealed class VcExchangeRepository
{
    private readonly BaseRepository _base;

    public VcExchangeRepository(BaseRepository baseRepository)
    {
        _base = baseRepository ?? throw new ArgumentNullException(nameof(baseRepository));
    }

    public async Task EnsureTableExistsAsync()
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS vc_exchange_accounts (
                guild_id BIGINT NOT NULL,
                user_id BIGINT NOT NULL,
                exchanged_seconds BIGINT NOT NULL DEFAULT 0 CHECK (exchanged_seconds >= 0),
                PRIMARY KEY (guild_id, user_id)
            );
            """;
        await _base.ExecuteNonQueryAsync(sql);
        Logger.Info("VcExchangeRepository: 換金テーブル確認・作成完了");
    }

    public async Task<VcExchangePreview> GetPreviewAsync(ulong guildId, ulong userId)
    {
        var totalSeconds = await GetTotalSecondsAsync(guildId, userId);
        var exchangedSeconds = await GetExchangedSecondsAsync(guildId, userId);
        return CreatePreview(guildId, userId, totalSeconds, exchangedSeconds);
    }

    public async Task<VcExchangeResult?> ExchangeAsync(ulong guildId, ulong userId)
    {
        VcExchangeResult result = null;
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic transaction = await connection.BeginTransactionAsync();
            try
            {
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO vc_exchange_accounts(guild_id, user_id)
                    VALUES (@guild_id, @user_id)
                    ON CONFLICT (guild_id, user_id) DO NOTHING;
                    """, guildId, userId);

                var totalSeconds = await ExecuteLongAsync(connection, transaction, """
                    SELECT COALESCE(SUM(duration_seconds), 0)
                    FROM vc_sessions
                    WHERE guild_id = @guild_id
                      AND user_id = @user_id
                      AND duration_seconds IS NOT NULL;
                    """, guildId, userId);
                var exchangedSeconds = await ExecuteLongAsync(connection, transaction, """
                    SELECT exchanged_seconds
                    FROM vc_exchange_accounts
                    WHERE guild_id = @guild_id AND user_id = @user_id
                    FOR UPDATE;
                    """, guildId, userId);
                var preview = CreatePreview(guildId, userId, totalSeconds, exchangedSeconds);
                if (!preview.CanExchange)
                {
                    await transaction.RollbackAsync();
                    return;
                }

                await ExecuteAsync(connection, transaction, """
                    UPDATE vc_exchange_accounts
                    SET exchanged_seconds = exchanged_seconds + @exchangeable_seconds
                    WHERE guild_id = @guild_id AND user_id = @user_id;
                    """, guildId, userId, ("@exchangeable_seconds", preview.ExchangeableSeconds));

                await ExecuteAsync(connection, transaction, """
                    INSERT INTO user_data(guild_id, user_id, coins)
                    VALUES (@guild_id, @user_id, @initial_coins)
                    ON CONFLICT (guild_id, user_id) DO NOTHING;
                    """, guildId, userId, ("@initial_coins", UserDataRepository.InitialCoins));
                var newBalance = await ExecuteLongAsync(connection, transaction, """
                    UPDATE user_data
                    SET coins = coins + @coins, updated_at = now()
                    WHERE guild_id = @guild_id AND user_id = @user_id
                    RETURNING coins;
                    """, guildId, userId, ("@coins", preview.Coins));
                await transaction.CommitAsync();
                result = new VcExchangeResult(preview, newBalance);
                Logger.Info("VcExchangeRepository: VC換金 user={UserId} guild={GuildId} seconds={Seconds} coins={Coins}", userId, guildId, preview.ExchangeableSeconds, preview.Coins);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
            finally
            {
                await transaction.DisposeAsync();
            }
        });
        return result;
    }

    private async Task<long> GetTotalSecondsAsync(ulong guildId, ulong userId)
        => await _base.UseConnectionAsync(async connection => await ExecuteLongAsync(connection, null, """
            SELECT COALESCE(SUM(duration_seconds), 0)
            FROM vc_sessions
            WHERE guild_id = @guild_id AND user_id = @user_id AND duration_seconds IS NOT NULL;
            """, guildId, userId));

    private async Task<long> GetExchangedSecondsAsync(ulong guildId, ulong userId)
        => await _base.UseConnectionAsync(async connection => await ExecuteLongAsync(connection, null, """
            SELECT COALESCE(exchanged_seconds, 0)
            FROM vc_exchange_accounts
            WHERE guild_id = @guild_id AND user_id = @user_id;
            """, guildId, userId));

    private static VcExchangePreview CreatePreview(ulong guildId, ulong userId, long totalSeconds, long exchangedSeconds)
    {
        var availableSeconds = Math.Max(0, totalSeconds - exchangedSeconds);
        var units = availableSeconds / VcExchangeSettings.ExchangeUnitSeconds;
        var exchangeableSeconds = units * VcExchangeSettings.ExchangeUnitSeconds;
        return new VcExchangePreview(
            guildId,
            userId,
            totalSeconds,
            exchangedSeconds,
            availableSeconds,
            exchangeableSeconds,
            units * VcExchangeSettings.ExchangeCoinsPerUnit
        );
    }

    private static async Task ExecuteAsync(dynamic connection, dynamic transaction, string sql, ulong firstId, ulong secondId, (string Name, object Value)? extra = null)
    {
        dynamic command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.Parameters.AddWithValue("@guild_id", (long)firstId);
        command.Parameters.AddWithValue("@user_id", (long)secondId);
        if (extra.HasValue)
            command.Parameters.AddWithValue(extra.Value.Name, extra.Value.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ExecuteLongAsync(dynamic connection, dynamic transaction, string sql, ulong firstId, ulong secondId)
    {
        dynamic command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.Parameters.AddWithValue("@guild_id", (long)firstId);
        command.Parameters.AddWithValue("@user_id", (long)secondId);
        return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task<long> ExecuteLongAsync(dynamic connection, dynamic transaction, string sql, ulong guildId, ulong userId, (string Name, object Value) extra)
    {
        dynamic command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.Parameters.AddWithValue("@guild_id", (long)guildId);
        command.Parameters.AddWithValue("@user_id", (long)userId);
        command.Parameters.AddWithValue(extra.Name, extra.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
