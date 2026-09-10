using System;
using System.Threading.Tasks;
using TacoDiscordBot.Models;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Repository;

public sealed class SlotRepository
{
    private const int StatisticsRowId = 1;
    private readonly BaseRepository _base;

    public SlotRepository(BaseRepository baseRepository)
    {
        _base = baseRepository ?? throw new ArgumentNullException(nameof(baseRepository));
    }

    // スロット統計を保持する共有テーブルと初期レコードを作成します。
    public async Task EnsureTablesExistAsync()
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS slot_statistics (
                id INTEGER PRIMARY KEY,
                total_spins BIGINT NOT NULL DEFAULT 0,
                last_hit_spin BIGINT NULL,
                longest_hit_interval BIGINT NOT NULL DEFAULT 0,
                shortest_hit_interval BIGINT NULL
            );
            INSERT INTO slot_statistics(id)
            VALUES (1)
            ON CONFLICT (id) DO NOTHING;
            """;

        await _base.ExecuteNonQueryAsync(sql);
        Logger.Info("SlotRepository: 統計テーブル確認・作成完了");
    }

    public async Task<SlotStatistics> RecordSpinAsync(bool isHit)
    {
        // 行ロック付きトランザクションで、同時実行時もスピン番号を正しく更新します。
        SlotStatistics statistics = null;

        await _base.UseConnectionAsync(async connection =>
        {
            dynamic transaction = await connection.BeginTransactionAsync();
            try
            {
                dynamic readCommand = connection.CreateCommand();
                readCommand.CommandText = """
                    SELECT total_spins, last_hit_spin, longest_hit_interval, shortest_hit_interval
                    FROM slot_statistics
                    WHERE id = 1
                    FOR UPDATE
                    """;
                readCommand.Transaction = transaction;

                dynamic reader = await readCommand.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    throw new InvalidOperationException("スロット統計レコードが存在しません。");

                var totalSpins = reader.GetInt64(0) + 1;
                long? lastHitSpin = reader.IsDBNull(1) ? null : reader.GetInt64(1);
                var longestInterval = reader.GetInt64(2);
                long? shortestInterval = reader.IsDBNull(3) ? null : reader.GetInt64(3);
                await reader.DisposeAsync();

                long? lastHitInterval = null;
                if (isHit)
                {
                    lastHitInterval = lastHitSpin.HasValue ? totalSpins - lastHitSpin.Value - 1 : null;
                    if (lastHitInterval.HasValue)
                    {
                        longestInterval = Math.Max(longestInterval, lastHitInterval.Value);
                        shortestInterval = shortestInterval.HasValue
                            ? Math.Min(shortestInterval.Value, lastHitInterval.Value)
                            : lastHitInterval.Value;
                    }

                    lastHitSpin = totalSpins;
                }

                dynamic updateCommand = connection.CreateCommand();
                updateCommand.CommandText = $"""
                    UPDATE slot_statistics
                    SET total_spins = {totalSpins},
                        last_hit_spin = {(lastHitSpin.HasValue ? lastHitSpin.Value.ToString() : "NULL")},
                        longest_hit_interval = {longestInterval},
                        shortest_hit_interval = {(shortestInterval.HasValue ? shortestInterval.Value.ToString() : "NULL")}
                    WHERE id = {StatisticsRowId}
                    """;
                updateCommand.Transaction = transaction;
                await updateCommand.ExecuteNonQueryAsync();
                await transaction.CommitAsync();

                statistics = new SlotStatistics
                {
                    TotalSpins = totalSpins,
                    LastHitSpin = lastHitSpin,
                    LongestHitInterval = longestInterval,
                    ShortestHitInterval = shortestInterval,
                    LastHitInterval = lastHitInterval
                };
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

        return statistics;
    }

    public async Task<SlotStatistics> GetStatisticsAsync()
    {
        // bot全体で共有する現在のスロット統計を取得します。
        SlotStatistics statistics = null;
        const string sql = """
            SELECT total_spins, last_hit_spin, longest_hit_interval, shortest_hit_interval
            FROM slot_statistics
            WHERE id = 1
            """;

        await _base.UseConnectionAsync(async connection =>
        {
            dynamic command = connection.CreateCommand();
            command.CommandText = sql;
            dynamic reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                throw new InvalidOperationException("スロット統計レコードが存在しません。");

            statistics = new SlotStatistics
            {
                TotalSpins = reader.GetInt64(0),
                LastHitSpin = reader.IsDBNull(1) ? null : reader.GetInt64(1),
                LongestHitInterval = reader.GetInt64(2),
                ShortestHitInterval = reader.IsDBNull(3) ? null : reader.GetInt64(3)
            };
            await reader.DisposeAsync();
        });

        return statistics;
    }
}
