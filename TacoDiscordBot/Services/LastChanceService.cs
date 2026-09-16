using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services.Interface;

namespace TacoDiscordBot.Services;

public sealed class LastChanceService
{
    public const long StableReward = 1_000;
    public const long JackpotReward = 5_000;
    private const int RandomRange = 100;
    private const int JackpotProbabilityPercent = 5;

    private readonly ILastChanceStore _store;
    private readonly ConcurrentDictionary<string, LastChanceGame> _games = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly Func<int> _nextRandom;
    private readonly RoleService _roleService;

    public LastChanceService(
        ILastChanceStore store,
        Func<int>? nextRandom = null,
        RoleService? roleService = null
    )
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _nextRandom = nextRandom ?? (() => Random.Shared.Next(RandomRange));
        _roleService = roleService;
    }

    public async Task StartAsync(ulong guildId, ulong userId)
    {
        using var gameLock = await EnterAsync(guildId, userId);
        var key = CreateGameKey(guildId, userId);
        if (_games.ContainsKey(key))
            throw new InvalidOperationException("⚠️ 現在 /lastchance を実行中です。");

        if (!await _store.TryStartAsync(guildId, userId))
            throw new InvalidOperationException("❌ /lastchance は所持コインが0のときのみ使用できます。");

        _games[key] = new LastChanceGame(guildId, userId);
        if (_roleService != null)
            await _roleService.RecordEventAsync(guildId, userId, "bankruptcy_count");
    }

    public async Task<LastChanceResult> SelectAsync(ulong guildId, ulong userId, LastChanceChoice choice)
    {
        using var gameLock = await EnterAsync(guildId, userId);
        var key = CreateGameKey(guildId, userId);
        if (!_games.ContainsKey(key))
            throw new InvalidOperationException("このラストチャンスは終了しています。");

        var reward = DetermineReward(choice);
        var balance = await _store.CompleteAsync(guildId, userId, reward);
        if (!balance.HasValue)
            throw new InvalidOperationException("コインの更新に失敗しました。ゲームは終了しています。");

        _games.TryRemove(key, out _);
        if (_roleService != null)
        {
            var conditionType = reward == JackpotReward
                ? "lastchance_jackpot_count"
                : reward == 0 ? "lastchance_zero_count" : null;
            if (conditionType != null)
                await _roleService.RecordEventAsync(guildId, userId, conditionType);
        }
        return new LastChanceResult(reward, balance.Value, choice == LastChanceChoice.Jackpot && reward == JackpotReward);
    }

    public static long DetermineGambleReward(int randomValue)
    {
        if (randomValue is < 0 or >= 100)
            throw new ArgumentOutOfRangeException(nameof(randomValue));

        return randomValue switch
        {
            < 10 => 0,
            < 35 => 500,
            < 65 => 1_000,
            < 85 => 1_500,
            < 95 => 2_000,
            _ => 3_000
        };
    }

    private long DetermineReward(LastChanceChoice choice)
        => choice switch
        {
            LastChanceChoice.Stable => StableReward,
            LastChanceChoice.Gamble => DetermineGambleReward(_nextRandom()),
            LastChanceChoice.Jackpot => _nextRandom() >= RandomRange - JackpotProbabilityPercent
                ? JackpotReward
                : 0,
            _ => throw new ArgumentOutOfRangeException(nameof(choice))
        };

    private async Task<IDisposable> EnterAsync(ulong guildId, ulong userId)
    {
        var gate = _locks.GetOrAdd(CreateGameKey(guildId, userId), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        return new Releaser(gate);
    }

    private static string CreateGameKey(ulong guildId, ulong userId) => $"{guildId}:{userId}";

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _gate;

        public Releaser(SemaphoreSlim gate) => _gate = gate;

        public void Dispose() => _gate.Release();
    }
}

public enum LastChanceChoice
{
    Stable,
    Gamble,
    Jackpot
}
