using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services.Interface;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Repository;

public sealed class UserDataRepository : ILastChanceStore
{
    public const long InitialCoins = 5000;
    private readonly BaseRepository _base;

    public UserDataRepository(BaseRepository baseRepository)
    {
        _base = baseRepository ?? throw new ArgumentNullException(nameof(baseRepository));
    }

    public async Task EnsureTablesExistAsync()
    {
        var sql = $"""
            CREATE TABLE IF NOT EXISTS user_data (
                guild_id BIGINT NOT NULL,
                user_id BIGINT NOT NULL,
                coins BIGINT NOT NULL DEFAULT {InitialCoins} CHECK (coins >= 0),
                lastchance_count BIGINT NOT NULL DEFAULT 0 CHECK (lastchance_count >= 0),
                blackjack_wins BIGINT NOT NULL DEFAULT 0 CHECK (blackjack_wins >= 0),
                blackjack_loss_half_units BIGINT NOT NULL DEFAULT 0 CHECK (blackjack_loss_half_units >= 0),
                blackjack_draws BIGINT NOT NULL DEFAULT 0 CHECK (blackjack_draws >= 0),
                created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (guild_id, user_id)
            );
            ALTER TABLE user_data
            ALTER COLUMN coins SET DEFAULT {InitialCoins};
            ALTER TABLE user_data
            ADD COLUMN IF NOT EXISTS lastchance_count BIGINT NOT NULL DEFAULT 0;
            ALTER TABLE user_data
            ADD COLUMN IF NOT EXISTS blackjack_wins BIGINT NOT NULL DEFAULT 0;
            ALTER TABLE user_data
            ADD COLUMN IF NOT EXISTS blackjack_loss_half_units BIGINT NOT NULL DEFAULT 0;
            ALTER TABLE user_data
            ADD COLUMN IF NOT EXISTS blackjack_draws BIGINT NOT NULL DEFAULT 0;
            CREATE TABLE IF NOT EXISTS shop_purchases (
                guild_id BIGINT NOT NULL,
                user_id BIGINT NOT NULL,
                role_key TEXT NOT NULL,
                purchased_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (guild_id, user_id, role_key)
            );
            """;
        await _base.ExecuteNonQueryAsync(sql);
        Logger.Info("UserDataRepository: サーバー単位ユーザーデータテーブル確認・作成完了");
    }

    public async Task<UserData> GetOrCreateAsync(ulong guildId, ulong userId)
    {
        UserData result = null;
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO user_data(guild_id, user_id)
                VALUES (@guild_id, @user_id)
                ON CONFLICT (guild_id, user_id) DO NOTHING;
                SELECT guild_id, user_id, coins, lastchance_count
                FROM user_data
                WHERE guild_id = @guild_id AND user_id = @user_id;
                """;
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            command.Parameters.AddWithValue("@user_id", (long)userId);
            dynamic reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                result = new UserData
                {
                    GuildId = (ulong)reader.GetInt64(0),
                    UserId = (ulong)reader.GetInt64(1),
                    Coins = reader.GetInt64(2),
                    LastChanceCount = reader.GetInt64(3)
                };
            }
            await reader.DisposeAsync();
        });

        return result ?? throw new InvalidOperationException("サーバー単位のユーザーデータを作成できませんでした。");
    }

    public async Task<bool> HasPurchasedShopRoleAsync(ulong guildId, ulong userId, string roleKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleKey);

        var exists = false;
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                SELECT EXISTS(
                    SELECT 1
                    FROM shop_purchases
                    WHERE guild_id = @guild_id AND user_id = @user_id AND role_key = @role_key
                );
                """;
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            command.Parameters.AddWithValue("@user_id", (long)userId);
            command.Parameters.AddWithValue("@role_key", roleKey);
            exists = (bool)await command.ExecuteScalarAsync();
        });

        return exists;
    }

    public async Task<bool> RecordShopRolePurchaseAsync(ulong guildId, ulong userId, string roleKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleKey);

        var recorded = false;
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO shop_purchases(guild_id, user_id, role_key)
                VALUES (@guild_id, @user_id, @role_key)
                ON CONFLICT (guild_id, user_id, role_key) DO NOTHING;
                """;
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            command.Parameters.AddWithValue("@user_id", (long)userId);
            command.Parameters.AddWithValue("@role_key", roleKey);
            recorded = await command.ExecuteNonQueryAsync() > 0;
        });

        return recorded;
    }

    public async Task<BlackjackStatistics> RecordBlackjackOutcomeAsync(
        ulong guildId,
        ulong userId,
        bool isWin,
        bool isDraw,
        long lossHalfUnits
    )
    {
        if (lossHalfUnits < 0 || lossHalfUnits > 2)
            throw new ArgumentOutOfRangeException(nameof(lossHalfUnits));

        await GetOrCreateAsync(guildId, userId);
        BlackjackStatistics result = null;
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                UPDATE user_data
                SET blackjack_wins = blackjack_wins + @wins,
                    blackjack_loss_half_units = blackjack_loss_half_units + @loss_half_units,
                    blackjack_draws = blackjack_draws + @draws,
                    updated_at = now()
                WHERE guild_id = @guild_id AND user_id = @user_id
                RETURNING blackjack_wins, blackjack_loss_half_units, blackjack_draws;
                """;
            command.Parameters.AddWithValue("@wins", isWin ? 1 : 0);
            command.Parameters.AddWithValue("@loss_half_units", lossHalfUnits);
            command.Parameters.AddWithValue("@draws", isDraw ? 1 : 0);
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            command.Parameters.AddWithValue("@user_id", (long)userId);
            dynamic reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                result = new BlackjackStatistics
                {
                    Wins = reader.GetInt64(0),
                    LossHalfUnits = reader.GetInt64(1),
                    Draws = reader.GetInt64(2)
                };
            }
            await reader.DisposeAsync();
        });

        return result ?? throw new InvalidOperationException("ブラックジャック戦績を更新できませんでした。");
    }

    public async Task<long> AddCoinsAsync(ulong guildId, ulong userId, long amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));

        await GetOrCreateAsync(guildId, userId);
        object value = null;
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                UPDATE user_data
                SET coins = coins + @amount, updated_at = now()
                WHERE guild_id = @guild_id AND user_id = @user_id
                RETURNING coins;
                """;
            command.Parameters.AddWithValue("@amount", amount);
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            command.Parameters.AddWithValue("@user_id", (long)userId);
            value = await command.ExecuteScalarAsync();
        });
        Logger.Info("UserDataRepository: コイン増加 guild={GuildId} user={UserId} amount={Amount}", guildId, userId, amount);
        return (long)value;
    }

    public async Task<long?> TryRemoveCoinsAsync(ulong guildId, ulong userId, long amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));

        await GetOrCreateAsync(guildId, userId);
        object value = null;
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                UPDATE user_data
                SET coins = coins - @amount, updated_at = now()
                WHERE guild_id = @guild_id AND user_id = @user_id AND coins >= @amount
                RETURNING coins;
                """;
            command.Parameters.AddWithValue("@amount", amount);
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            command.Parameters.AddWithValue("@user_id", (long)userId);
            value = await command.ExecuteScalarAsync();
        });
        if (value == null || value == DBNull.Value)
            return null;

        Logger.Info("UserDataRepository: コイン減少 guild={GuildId} user={UserId} amount={Amount}", guildId, userId, amount);
        return (long)value;
    }

    public async Task<bool> TransferAsync(ulong guildId, ulong senderId, ulong receiverId, long amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (senderId == receiverId)
            throw new ArgumentException("送信者と受信者は異なる必要があります。", nameof(receiverId));

        var transferred = await _base.UseTransactionAsync<bool>(async (connection, transaction) =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO user_data(guild_id, user_id)
                VALUES (@guild_id, @sender_id), (@guild_id, @receiver_id)
                ON CONFLICT (guild_id, user_id) DO NOTHING;

                WITH deducted AS (
                    UPDATE user_data
                    SET coins = coins - @amount, updated_at = now()
                    WHERE guild_id = @guild_id AND user_id = @sender_id AND coins >= @amount
                    RETURNING guild_id
                )
                UPDATE user_data AS receiver
                SET coins = receiver.coins + @amount, updated_at = now()
                FROM deducted
                WHERE receiver.guild_id = @guild_id AND receiver.user_id = @receiver_id
                RETURNING receiver.coins;
                """;
            command.Transaction = transaction;
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            command.Parameters.AddWithValue("@sender_id", (long)senderId);
            command.Parameters.AddWithValue("@receiver_id", (long)receiverId);
            command.Parameters.AddWithValue("@amount", amount);

            dynamic reader = await command.ExecuteReaderAsync();
            var result = await reader.ReadAsync();
            await reader.DisposeAsync();
            return result;
        });

        if (transferred)
        {
            Logger.Info("UserDataRepository: コイン送金 guild={GuildId} sender={SenderId} receiver={ReceiverId} amount={Amount}",
                guildId, senderId, receiverId, amount);
        }

        return transferred;
    }

    public async Task<bool> TryStartReversiBetAsync(ulong guildId, ulong blackPlayerId, ulong whitePlayerId, long betAmount)
    {
        if (betAmount <= 0)
            throw new ArgumentOutOfRangeException(nameof(betAmount));
        return await _base.UseTransactionAsync<bool>(async (connection, transaction) =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO user_data(guild_id, user_id)
                VALUES (@guild_id, @black_id), (@guild_id, @white_id)
                ON CONFLICT (guild_id, user_id) DO NOTHING;

                WITH eligible AS (
                    SELECT user_id
                    FROM user_data
                    WHERE guild_id = @guild_id
                      AND user_id IN (@black_id, @white_id)
                      AND coins >= @bet_amount
                ), deducted AS (
                    UPDATE user_data
                    SET coins = coins - @bet_amount, updated_at = now()
                    WHERE guild_id = @guild_id
                      AND user_id IN (SELECT user_id FROM eligible)
                      AND (SELECT COUNT(*) FROM eligible) = 2
                    RETURNING user_id
                )
                SELECT COUNT(*) FROM deducted;
                """;
            command.Transaction = transaction;
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            command.Parameters.AddWithValue("@black_id", (long)blackPlayerId);
            command.Parameters.AddWithValue("@white_id", (long)whitePlayerId);
            command.Parameters.AddWithValue("@bet_amount", betAmount);

            dynamic reader = await command.ExecuteReaderAsync();
            var success = await reader.ReadAsync() && reader.GetInt64(0) == 2;
            await reader.DisposeAsync();
            return success;
        });
    }

    public async Task<bool> SettleReversiBetAsync(
        ulong guildId,
        ulong blackPlayerId,
        ulong whitePlayerId,
        ulong? winnerId,
        long betAmount,
        long additionalLoss,
        long payout
    )
    {
        if (betAmount <= 0 || additionalLoss < 0 || payout <= 0)
            throw new ArgumentOutOfRangeException(nameof(betAmount));

        return await _base.UseTransactionAsync<bool>(async (connection, transaction) =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = winnerId.HasValue
                ? """
                    WITH deducted AS (
                        UPDATE user_data
                        SET coins = GREATEST(coins - @additional_loss, 0), updated_at = now()
                        WHERE guild_id = @guild_id AND user_id = @loser_id
                        RETURNING guild_id
                    )
                    UPDATE user_data AS winner
                    SET coins = winner.coins + @payout, updated_at = now()
                    FROM deducted
                    WHERE winner.guild_id = @guild_id AND winner.user_id = @winner_id
                    RETURNING TRUE;
                    """
                : """
                    UPDATE user_data
                    SET coins = coins + @bet_amount, updated_at = now()
                    WHERE guild_id = @guild_id AND user_id IN (@black_id, @white_id);

                    SELECT TRUE;
                    """;
            command.Transaction = transaction;
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            command.Parameters.AddWithValue("@black_id", (long)blackPlayerId);
            command.Parameters.AddWithValue("@white_id", (long)whitePlayerId);
            command.Parameters.AddWithValue("@bet_amount", betAmount);
            command.Parameters.AddWithValue("@additional_loss", additionalLoss);
            command.Parameters.AddWithValue("@payout", payout);
            if (winnerId.HasValue)
            {
                var loserId = winnerId.Value == blackPlayerId ? whitePlayerId : blackPlayerId;
                command.Parameters.AddWithValue("@winner_id", (long)winnerId.Value);
                command.Parameters.AddWithValue("@loser_id", (long)loserId);
            }

            dynamic reader = await command.ExecuteReaderAsync();
            var success = await reader.ReadAsync() && reader.GetBoolean(0);
            await reader.DisposeAsync();
            return success;
        });
    }

    public async Task<List<UserData>> GetAllAsync(ulong guildId)
        => await GetUsersAsync(guildId, null);

    public async Task<List<UserData>> GetTopAsync(ulong guildId, int limit)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit));

        return await GetUsersAsync(guildId, limit);
    }

    private async Task<List<UserData>> GetUsersAsync(ulong guildId, int? limit)
    {
        var result = new List<UserData>();
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            var limitClause = limit.HasValue ? "LIMIT @limit" : string.Empty;
            command.CommandText = $"""
                SELECT guild_id, user_id, coins, lastchance_count
                FROM user_data
                WHERE guild_id = @guild_id
                ORDER BY coins DESC
                {limitClause};
                """;
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            if (limit.HasValue)
                command.Parameters.AddWithValue("@limit", limit.Value);

            dynamic reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new UserData
                {
                    GuildId = (ulong)reader.GetInt64(0),
                    UserId = (ulong)reader.GetInt64(1),
                    Coins = reader.GetInt64(2),
                    LastChanceCount = reader.GetInt64(3)
                });
            }
            await reader.DisposeAsync();
        });
        return result;
    }

    public async Task<bool> TryStartAsync(ulong guildId, ulong userId)
    {
        await GetOrCreateAsync(guildId, userId);
        return await _base.UseTransactionAsync<bool>(async (connection, transaction) =>
        {
            dynamic command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE user_data
                SET lastchance_count = lastchance_count + 1, updated_at = now()
                WHERE guild_id = @guild_id AND user_id = @user_id AND coins = 0
                RETURNING lastchance_count;
                """;
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            command.Parameters.AddWithValue("@user_id", (long)userId);
            return (await command.ExecuteScalarAsync()) != null;
        });
    }

    public async Task<long?> CompleteAsync(ulong guildId, ulong userId, long reward)
    {
        if (reward < 0)
            throw new ArgumentOutOfRangeException(nameof(reward));

        await GetOrCreateAsync(guildId, userId);
        object value = null;
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                UPDATE user_data
                SET coins = coins + @reward, updated_at = now()
                WHERE guild_id = @guild_id AND user_id = @user_id AND coins = 0
                RETURNING coins;
                """;
            command.Parameters.AddWithValue("@reward", reward);
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            command.Parameters.AddWithValue("@user_id", (long)userId);
            value = await command.ExecuteScalarAsync();
        });

        return value == null || value == DBNull.Value ? null : (long)value;
    }
}
