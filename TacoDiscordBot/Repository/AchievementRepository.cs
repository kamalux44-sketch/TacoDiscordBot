using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TacoDiscordBot.Models;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Repository;

public sealed class AchievementRepository
{
    private readonly BaseRepository _base;

    public AchievementRepository(BaseRepository baseRepository)
    {
        _base = baseRepository ?? throw new ArgumentNullException(nameof(baseRepository));
    }

    public async Task EnsureTablesExistAsync()
    {
        var sql = """
            CREATE TABLE IF NOT EXISTS guild_settings (
                guild_id BIGINT PRIMARY KEY,
                role_notification_channel_id BIGINT NULL,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE TABLE IF NOT EXISTS achievement_definitions (
                id BIGSERIAL PRIMARY KEY,
                role_id BIGINT NULL,
                role_name TEXT NOT NULL,
                condition_type TEXT NOT NULL,
                group_key TEXT NULL,
                threshold BIGINT NOT NULL CHECK (threshold > 0),
                rarity TEXT NOT NULL DEFAULT 'normal',
                parent_achievement_id BIGINT NULL REFERENCES achievement_definitions(id),
                condition_description TEXT NOT NULL,
                UNIQUE (condition_type, threshold, role_name)
            );
            CREATE TABLE IF NOT EXISTS user_achievement_stats (
                guild_id BIGINT NOT NULL,
                user_id BIGINT NOT NULL,
                bankruptcy_count BIGINT NOT NULL DEFAULT 0,
                rare_slot_count BIGINT NOT NULL DEFAULT 0,
                max_mines_safe_count BIGINT NOT NULL DEFAULT 0,
                lastchance_jackpot_count BIGINT NOT NULL DEFAULT 0,
                lastchance_zero_count BIGINT NOT NULL DEFAULT 0,
                blackjack_win_streak BIGINT NOT NULL DEFAULT 0,
                blackjack_loss_streak BIGINT NOT NULL DEFAULT 0,
                PRIMARY KEY (guild_id, user_id)
            );
            ALTER TABLE user_achievement_stats ADD COLUMN IF NOT EXISTS lastchance_jackpot_count BIGINT NOT NULL DEFAULT 0;
            ALTER TABLE user_achievement_stats ADD COLUMN IF NOT EXISTS lastchance_zero_count BIGINT NOT NULL DEFAULT 0;
            ALTER TABLE user_achievement_stats ADD COLUMN IF NOT EXISTS blackjack_win_streak BIGINT NOT NULL DEFAULT 0;
            ALTER TABLE user_achievement_stats ADD COLUMN IF NOT EXISTS blackjack_loss_streak BIGINT NOT NULL DEFAULT 0;
            ALTER TABLE achievement_definitions ADD COLUMN IF NOT EXISTS group_key TEXT NULL;
            UPDATE achievement_definitions
            SET group_key = CASE role_name
                WHEN '破産者' THEN 'bankruptcy'
                WHEN '破滅者' THEN 'bankruptcy'
                WHEN '人間未満' THEN 'bankruptcy'
                WHEN 'ATM' THEN 'bankruptcy'
                WHEN '歩く負債' THEN 'bankruptcy'
                WHEN '概念' THEN 'bankruptcy'
                WHEN '奇跡の復活者' THEN 'lastchance'
                WHEN '死に損ない' THEN 'lastchance'
                WHEN '大富豪' THEN 'winning'
                WHEN '金持ち' THEN 'winning'
                WHEN '神に愛された者' THEN 'winning'
                WHEN 'ギャンブルの申し子' THEN 'winning'
                WHEN '大貧民' THEN 'losing'
                WHEN '連勝街道' THEN 'blackjack_win'
                WHEN '勝ち馬' THEN 'blackjack_win'
                WHEN '全戦全勝' THEN 'blackjack_win'
                WHEN '負け癖' THEN 'blackjack_loss'
                WHEN '負け街道' THEN 'blackjack_loss'
                WHEN '底なし沼' THEN 'blackjack_loss'
                ELSE group_key
            END
            WHERE group_key IS NULL;
            CREATE TABLE IF NOT EXISTS granted_achievements (
                guild_id BIGINT NOT NULL,
                user_id BIGINT NOT NULL,
                achievement_id BIGINT NOT NULL REFERENCES achievement_definitions(id),
                granted_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (guild_id, user_id, achievement_id)
            );
            CREATE TABLE IF NOT EXISTS guild_achievement_roles (
                guild_id BIGINT NOT NULL,
                achievement_id BIGINT NOT NULL REFERENCES achievement_definitions(id),
                role_id BIGINT NOT NULL,
                PRIMARY KEY (guild_id, achievement_id)
            );
            INSERT INTO achievement_definitions
                (role_name, condition_type, group_key, threshold, rarity, condition_description)
            VALUES
                ('破産者', 'bankruptcy_count', 'bankruptcy', 1, 'normal', '破産回数 1回達成'),
                ('破滅者', 'bankruptcy_count', 'bankruptcy', 5, 'normal', '破産回数 5回達成'),
                ('人間未満', 'bankruptcy_count', 'bankruptcy', 10, 'normal', '破産回数 10回達成'),
                ('ATM', 'bankruptcy_count', 'bankruptcy', 50, 'normal', '破産回数 50回達成'),
                ('歩く負債', 'bankruptcy_count', 'bankruptcy', 100, 'normal', '破産回数 100回達成'),
                ('概念', 'bankruptcy_count', 'bankruptcy', 500, 'normal', '破産回数 500回達成'),
                ('奇跡の復活者', 'lastchance_jackpot_count', 'lastchance', 1, 'rare', 'ラストチャンスで5000コイン獲得'),
                ('死に損ない', 'lastchance_zero_count', 'lastchance', 1, 'normal', 'ラストチャンスで0コイン獲得'),
                ('大富豪', 'coins', 'winning', 1000000, 'normal', '所持コイン100万以上'),
                ('金持ち', 'coins', 'winning', 100000, 'normal', '所持コイン10万以上'),
                ('神に愛された者', 'god_achievement', 'winning', 1, 'rare', 'スロットでレア当選、またはMinesで15マス以上開放'),
                ('ギャンブルの申し子', 'coins', 'winning', 50000, 'normal', '所持コイン5万以上'),
                ('大貧民', 'lowest_balance', 'losing', 1, 'normal', '所持金ランキング最下位'),
                ('連勝街道', 'blackjack_win_streak', 'blackjack_win', 5, 'normal', 'ブラックジャック5連勝'),
                ('勝ち馬', 'blackjack_win_streak', 'blackjack_win', 10, 'normal', 'ブラックジャック10連勝'),
                ('全戦全勝', 'blackjack_win_streak', 'blackjack_win', 20, 'normal', 'ブラックジャック20連勝'),
                ('負け癖', 'blackjack_loss_streak', 'blackjack_loss', 5, 'normal', 'ブラックジャック5連敗'),
                ('負け街道', 'blackjack_loss_streak', 'blackjack_loss', 10, 'normal', 'ブラックジャック10連敗'),
                ('底なし沼', 'blackjack_loss_streak', 'blackjack_loss', 20, 'normal', 'ブラックジャック20連敗')
            ON CONFLICT (condition_type, threshold, role_name) DO NOTHING;
            """;
        await _base.ExecuteNonQueryAsync(sql);
        Logger.Info("AchievementRepository: 実績関連テーブル確認・作成完了");
    }

    public async Task SetNotificationChannelAsync(ulong guildId, ulong channelId)
    {
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO guild_settings(guild_id, role_notification_channel_id)
                VALUES (@guild_id, @channel_id)
                ON CONFLICT (guild_id) DO UPDATE
                SET role_notification_channel_id = EXCLUDED.role_notification_channel_id,
                    updated_at = now();
                """;
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            command.Parameters.AddWithValue("@channel_id", (long)channelId);
            await command.ExecuteNonQueryAsync();
        });
    }

    public async Task<ulong?> GetNotificationChannelAsync(ulong guildId)
    {
        object value = null;
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = "SELECT role_notification_channel_id FROM guild_settings WHERE guild_id = @guild_id;";
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            value = await command.ExecuteScalarAsync();
        });
        return value is null or DBNull ? null : (ulong)(long)value;
    }

    public async Task RemoveNotificationChannelAsync(ulong guildId)
    {
        await _base.ExecuteNonQueryAsync(
            $"UPDATE guild_settings SET role_notification_channel_id = NULL, updated_at = now() WHERE guild_id = {(long)guildId}"
        );
    }

    public async Task<IReadOnlyList<ulong>> GetConfiguredGuildIdsAsync()
    {
        var result = new List<ulong>();
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                SELECT guild_id
                FROM guild_settings
                WHERE role_notification_channel_id IS NOT NULL;
                """;
            dynamic reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add((ulong)reader.GetInt64(0));
            await reader.DisposeAsync();
        });
        return result;
    }

    public async Task<ulong?> GetGuildRoleIdAsync(ulong guildId, long achievementId)
    {
        object value = null;
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                SELECT role_id
                FROM guild_achievement_roles
                WHERE guild_id = @guild_id AND achievement_id = @achievement_id;
                """;
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            command.Parameters.AddWithValue("@achievement_id", achievementId);
            value = await command.ExecuteScalarAsync();
        });
        return value is null or DBNull ? null : (ulong)(long)value;
    }

    public async Task SetGuildRoleIdAsync(ulong guildId, long achievementId, ulong roleId)
    {
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO guild_achievement_roles(guild_id, achievement_id, role_id)
                VALUES (@guild_id, @achievement_id, @role_id)
                ON CONFLICT (guild_id, achievement_id) DO UPDATE
                SET role_id = EXCLUDED.role_id;
                """;
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            command.Parameters.AddWithValue("@achievement_id", achievementId);
            command.Parameters.AddWithValue("@role_id", (long)roleId);
            await command.ExecuteNonQueryAsync();
        });
    }

    public async Task<AchievementStats> IncrementStatAsync(ulong guildId, ulong userId, string conditionType, long amount = 1)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));

        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO user_achievement_stats(guild_id, user_id)
                VALUES (@guild_id, @user_id)
                ON CONFLICT (guild_id, user_id) DO NOTHING;
                UPDATE user_achievement_stats
                SET bankruptcy_count = CASE WHEN @condition_type = 'bankruptcy_count' THEN bankruptcy_count + @amount ELSE bankruptcy_count END,
                    rare_slot_count = CASE WHEN @condition_type = 'rare_slot_count' THEN rare_slot_count + @amount ELSE rare_slot_count END,
                    max_mines_safe_count = CASE WHEN @condition_type = 'mines_safe_count' THEN GREATEST(max_mines_safe_count, @amount) ELSE max_mines_safe_count END,
                    lastchance_jackpot_count = CASE WHEN @condition_type = 'lastchance_jackpot_count' THEN lastchance_jackpot_count + @amount ELSE lastchance_jackpot_count END,
                    lastchance_zero_count = CASE WHEN @condition_type = 'lastchance_zero_count' THEN lastchance_zero_count + @amount ELSE lastchance_zero_count END,
                    blackjack_win_streak = CASE
                        WHEN @condition_type = 'blackjack_win_streak' THEN blackjack_win_streak + @amount
                        WHEN @condition_type = 'blackjack_loss_streak' THEN 0
                        ELSE blackjack_win_streak
                    END,
                    blackjack_loss_streak = CASE
                        WHEN @condition_type = 'blackjack_loss_streak' THEN blackjack_loss_streak + @amount
                        WHEN @condition_type = 'blackjack_win_streak' THEN 0
                        ELSE blackjack_loss_streak
                    END
                WHERE guild_id = @guild_id AND user_id = @user_id;
                """;
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            command.Parameters.AddWithValue("@user_id", (long)userId);
            command.Parameters.AddWithValue("@condition_type", conditionType);
            command.Parameters.AddWithValue("@amount", amount);
            await command.ExecuteNonQueryAsync();
        });

        return await GetStatsAsync(guildId, userId);
    }

    public async Task<AchievementStats> GetStatsAsync(ulong guildId, ulong userId)
    {
        AchievementStats result = null;
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                SELECT bankruptcy_count, rare_slot_count, max_mines_safe_count,
                       lastchance_jackpot_count, lastchance_zero_count,
                       blackjack_win_streak, blackjack_loss_streak
                FROM user_achievement_stats
                WHERE guild_id = @guild_id AND user_id = @user_id;
                """;
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            command.Parameters.AddWithValue("@user_id", (long)userId);
            dynamic reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                result = new AchievementStats
                {
                    BankruptcyCount = reader.GetInt64(0),
                    RareSlotCount = reader.GetInt64(1),
                    MaxMinesSafeCount = reader.GetInt64(2),
                    LastChanceJackpotCount = reader.GetInt64(3),
                    LastChanceZeroCount = reader.GetInt64(4),
                    BlackjackWinStreak = reader.GetInt64(5),
                    BlackjackLossStreak = reader.GetInt64(6)
                };
            }
            await reader.DisposeAsync();
        });
        return result ?? new AchievementStats();
    }

    public async Task<IReadOnlyList<AchievementDefinition>> GetDefinitionsAsync()
    {
        var result = new List<AchievementDefinition>();
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, role_id, role_name, condition_type, group_key, threshold, rarity,
                       parent_achievement_id, condition_description
                FROM achievement_definitions
                ORDER BY id;
                """;
            dynamic reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new AchievementDefinition
                {
                    Id = reader.GetInt64(0),
                    RoleId = reader.IsDBNull(1) ? null : (ulong?)reader.GetInt64(1),
                    RoleName = reader.GetString(2),
                    ConditionType = reader.GetString(3),
                    GroupKey = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Threshold = reader.GetInt64(5),
                    Rarity = reader.GetString(6),
                    ParentAchievementId = reader.IsDBNull(7) ? null : reader.GetInt64(7),
                    ConditionDescription = reader.GetString(8)
                });
            }
            await reader.DisposeAsync();
        });
        return result;
    }

    public async Task<bool> TryRecordGrantAsync(ulong guildId, ulong userId, long achievementId)
    {
        var inserted = false;
        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO granted_achievements(guild_id, user_id, achievement_id)
                VALUES (@guild_id, @user_id, @achievement_id)
                ON CONFLICT DO NOTHING;
                """;
            command.Parameters.AddWithValue("@guild_id", (long)guildId);
            command.Parameters.AddWithValue("@user_id", (long)userId);
            command.Parameters.AddWithValue("@achievement_id", achievementId);
            inserted = await command.ExecuteNonQueryAsync() > 0;
        });
        return inserted;
    }
}
