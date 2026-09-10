using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TacoDiscordBot.Models;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Repository;

public sealed class BirthdayRepository
{
    private readonly BaseRepository _base;

    public BirthdayRepository(BaseRepository baseRepository)
    {
        _base = baseRepository ?? throw new ArgumentNullException(nameof(baseRepository));
    }

    public async Task EnsureTablesExistAsync()
    {
        Logger.Info("BirthdayRepository: テーブル確認・作成開始");
        const string sql = """
            CREATE TABLE IF NOT EXISTS birthdays (
                user_id BIGINT PRIMARY KEY,
                year INTEGER NULL,
                month INTEGER NOT NULL,
                day INTEGER NOT NULL,
                CHECK (month BETWEEN 1 AND 12),
                CHECK (day BETWEEN 1 AND 31)
            );
            CREATE TABLE IF NOT EXISTS birthday_channels (
                guild_id BIGINT PRIMARY KEY,
                channel_id BIGINT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS birthday_posts (
                guild_id BIGINT NOT NULL,
                user_id BIGINT NOT NULL,
                birthday_year INTEGER NOT NULL,
                posted_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (guild_id, user_id, birthday_year)
            );
            """;

        await _base.ExecuteNonQueryAsync(sql);
        Logger.Info("BirthdayRepository: テーブル確認・作成完了");
    }

    public async Task UpsertAsync(ulong userId, int? year, int month, int day)
    {
        Logger.Info("BirthdayRepository: 誕生日を登録または更新 user={UserId} year={Year} month={Month} day={Day}",
            userId, year, month, day);
        var yearSql = year.HasValue ? year.Value.ToString() : "NULL";
        var sql = $"""
            INSERT INTO birthdays(user_id, year, month, day)
            VALUES ({(long)userId}, {yearSql}, {month}, {day})
            ON CONFLICT (user_id) DO UPDATE SET
                year = EXCLUDED.year,
                month = EXCLUDED.month,
                day = EXCLUDED.day
            """;

        await _base.ExecuteNonQueryAsync(sql);
        Logger.Info("BirthdayRepository: 誕生日の登録または更新完了 user={UserId}", userId);
    }

    public async Task SetChannelAsync(ulong guildId, ulong channelId)
    {
        Logger.Info("BirthdayRepository: 投稿先を登録または更新 guild={GuildId} channel={ChannelId}", guildId, channelId);
        var sql = $"""
            INSERT INTO birthday_channels(guild_id, channel_id)
            VALUES ({(long)guildId}, {(long)channelId})
            ON CONFLICT (guild_id) DO UPDATE SET channel_id = EXCLUDED.channel_id
            """;

        await _base.ExecuteNonQueryAsync(sql);
        Logger.Info("BirthdayRepository: 投稿先の登録または更新完了 guild={GuildId}", guildId);
    }

    public async Task<IReadOnlyList<ulong>> GetChannelIdsAsync()
    {
        var channelIds = new List<ulong>();
        const string sql = "SELECT channel_id FROM birthday_channels";

        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = sql;
            dynamic reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                channelIds.Add((ulong)reader.GetInt64(0));
            }

            await reader.DisposeAsync();
        });

        Logger.Info("BirthdayRepository: 投稿先取得完了 count={Count}", channelIds.Count);
        return channelIds;
    }

    public async Task<IReadOnlyList<BirthdayRecord>> GetByMonthAndDayAsync(int month, int day)
    {
        var records = new List<BirthdayRecord>();
        var sql = $"SELECT user_id, year, month, day FROM birthdays WHERE month = {month} AND day = {day}";

        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = sql;
            dynamic reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                int? year = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                records.Add(new BirthdayRecord
                {
                    UserId = (ulong)reader.GetInt64(0),
                    Year = year,
                    Month = reader.GetInt32(2),
                    Day = reader.GetInt32(3)
                });
            }

            await reader.DisposeAsync();
        });

        Logger.Info("BirthdayRepository: 誕生日対象取得完了 month={Month} day={Day} count={Count}",
            month, day, records.Count);
        return records;
    }

    public async Task<bool> TryRecordPostAsync(ulong guildId, ulong userId, int birthdayYear)
    {
        var sql = $"""
            INSERT INTO birthday_posts(guild_id, user_id, birthday_year)
            VALUES ({(long)guildId}, {(long)userId}, {birthdayYear})
            ON CONFLICT (guild_id, user_id, birthday_year) DO NOTHING
            """;

        var inserted = false;
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = sql;
            inserted = await command.ExecuteNonQueryAsync() > 0;
        });

        Logger.Info("BirthdayRepository: 投稿済み記録結果 guild={GuildId} user={UserId} year={Year} inserted={Inserted}",
            guildId, userId, birthdayYear, inserted);
        return inserted;
    }

    public async Task RemovePostAsync(ulong guildId, ulong userId, int birthdayYear)
    {
        var sql = $"""
            DELETE FROM birthday_posts
            WHERE guild_id = {(long)guildId}
              AND user_id = {(long)userId}
              AND birthday_year = {birthdayYear}
            """;

        await _base.ExecuteNonQueryAsync(sql);
        Logger.Info("BirthdayRepository: 投稿済み記録を削除 guild={GuildId} user={UserId} year={Year}",
            guildId, userId, birthdayYear);
    }
}
