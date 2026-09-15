using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TacoDiscordBot.Models;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Repository;

public sealed class UserDataRepository
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
                created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (guild_id, user_id)
            );
            ALTER TABLE user_data
            ALTER COLUMN coins SET DEFAULT {InitialCoins};
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
                SELECT guild_id, user_id, coins
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
                    Coins = reader.GetInt64(2)
                };
            }
            await reader.DisposeAsync();
        });

        return result ?? throw new InvalidOperationException("サーバー単位のユーザーデータを作成できませんでした。");
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

    public async Task<List<UserData>> GetAllAsync(ulong guildId)
    {
        var result = new List<UserData>();
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                SELECT guild_id, user_id, coins
                FROM user_data
                WHERE guild_id = @guild_id
                ORDER BY coins DESC;
                """;
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            dynamic reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new UserData
                {
                    GuildId = (ulong)reader.GetInt64(0),
                    UserId = (ulong)reader.GetInt64(1),
                    Coins = reader.GetInt64(2)
                });
            }
            await reader.DisposeAsync();
        });
        return result;
    }
}
