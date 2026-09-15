using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TacoDiscordBot.Models;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Repository;

public sealed class UserDataRepository
{
    public const long InitialCoins = 1000;
    private readonly BaseRepository _base;

    public UserDataRepository(BaseRepository baseRepository)
    {
        _base = baseRepository ?? throw new ArgumentNullException(nameof(baseRepository));
    }

    public async Task EnsureTablesExistAsync()
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS user_data (
                user_id BIGINT PRIMARY KEY,
                coins BIGINT NOT NULL DEFAULT 1000 CHECK (coins >= 0),
                created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """;

        await _base.ExecuteNonQueryAsync(sql);
        Logger.Info("UserDataRepository: ユーザーデータテーブル確認・作成完了");
    }

    public async Task<UserData> GetOrCreateAsync(ulong userId)
    {
        UserData result = null;
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO user_data(user_id)
                VALUES (@user_id)
                ON CONFLICT (user_id) DO NOTHING;
                SELECT user_id, coins FROM user_data WHERE user_id = @user_id;
                """;
            command.Parameters.AddWithValue("@user_id", (long)userId);
            dynamic reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                result = new UserData { UserId = (ulong)reader.GetInt64(0), Coins = reader.GetInt64(1) };
            }
            await reader.DisposeAsync();
        });

        return result ?? throw new InvalidOperationException("ユーザーデータを作成できませんでした。");
    }

    public async Task<long> AddCoinsAsync(ulong userId, long amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));

        await GetOrCreateAsync(userId);
        object value = null;
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                UPDATE user_data
                SET coins = coins + @amount, updated_at = now()
                WHERE user_id = @user_id
                RETURNING coins;
                """;
            command.Parameters.AddWithValue("@amount", amount);
            command.Parameters.AddWithValue("@user_id", (long)userId);
            value = await command.ExecuteScalarAsync();
        });
        Logger.Info("UserDataRepository: コイン増加 user={UserId} amount={Amount}", userId, amount);
        return (long)value;
    }

    public async Task<long?> TryRemoveCoinsAsync(ulong userId, long amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));

        await GetOrCreateAsync(userId);
        object value = null;
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                UPDATE user_data
                SET coins = coins - @amount, updated_at = now()
                WHERE user_id = @user_id AND coins >= @amount
                RETURNING coins;
                """;
            command.Parameters.AddWithValue("@amount", amount);
            command.Parameters.AddWithValue("@user_id", (long)userId);
            value = await command.ExecuteScalarAsync();
        });
        if (value == null || value == DBNull.Value)
            return null;

        Logger.Info("UserDataRepository: コイン減少 user={UserId} amount={Amount}", userId, amount);
        return (long)value;
    }

    public async Task<List<UserData>> GetAllAsync()
    {
        var result = new List<UserData>();
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = "SELECT user_id, coins FROM user_data ORDER BY coins DESC";
            dynamic reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new UserData { UserId = (ulong)reader.GetInt64(0), Coins = reader.GetInt64(1) });
            }
            await reader.DisposeAsync();
        });
        return result;
    }
}
