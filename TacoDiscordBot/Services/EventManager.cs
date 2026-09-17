using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TacoDiscordBot.Models;
using TacoDiscordBot.Repository;
using TacoDiscordBot.Services.Interface;

namespace TacoDiscordBot.Services;

public sealed class EventManager : IAsyncDisposable
{
    private const int TopRankingLimit = 10;
    private const decimal DoublePayoutRate = 0.5m;
    private const decimal HotSlotRate = 0.5m;
    private const decimal LossBackRate = 0.3m;
    private const decimal GoodLuckRate = 0.1m;
    private const long BlackjackInsuranceCost = 10_000;
    private const long DoubleUpBoostCost = 20_000;
    private const decimal MinesSafetyOneRate = 0.2m;
    private const decimal MinesSafetyTwoRate = 0.4m;
    private const decimal MinesSafetyThreeRate = 0.95m;
    private const decimal LiveOrDieRate = 0.5m;
    private const decimal BlackjackInsuranceRate = 0.5m;
    private const decimal BlackjackInsuranceRefundRate = 0.8m;
    private const decimal LiveOrDieMultiplier = 5m;
    private readonly ICoinService _coinService;
    private readonly ServerEventRepository? _repository;
    private readonly Func<ServerEvent, bool, Task>? _notification;
    private readonly ConcurrentDictionary<ulong, ServerEvent> _activeEvents = new();
    private readonly ConcurrentDictionary<string, ServerEvent> _personalEvents = new();
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _monitorTask;

    public EventManager(
        ICoinService coinService,
        Func<ServerEvent, bool, Task>? notification = null,
        ServerEventRepository? repository = null)
    {
        _coinService = coinService ?? throw new ArgumentNullException(nameof(coinService));
        _notification = notification;
        _repository = repository;
    }

    public async Task RestoreAsync()
    {
        if (_repository == null)
            return;
        foreach (var serverEvent in await _repository.GetActiveAsync())
        {
            if (serverEvent.IsPersonal)
                _personalEvents[CreatePersonalKey(serverEvent.GuildId, serverEvent.ActivatorId)] = serverEvent;
            else if (serverEvent.IsActive(DateTimeOffset.UtcNow))
                _activeEvents[serverEvent.GuildId] = serverEvent;
        }
    }

    public static IReadOnlyList<EventDefinition> Definitions { get; } =
    [
        new(EventType.DoublePayout, "💰 倍返しキャンペーン！", "30分間、blackjack・doubleup・slot・rouletteの払い戻し1.5倍", TimeSpan.FromMinutes(30), DoublePayoutRate, 0),
        new(EventType.HotSlot, "🎰 激アツスロット×10！", "10分間、スロットの高額絵柄出現率アップ", TimeSpan.FromMinutes(10), HotSlotRate, 0),
        new(EventType.LossBack, "🛡️ 50% BACK保証", "30分間、対象ゲームの敗北時にベットの50%を返還", TimeSpan.FromMinutes(30), LossBackRate, 0),
        new(EventType.GoodLuck, "🍀 豪運に幸あれ！", "20分間、blackjack・slot・rouletteの払い戻し1.25倍", TimeSpan.FromMinutes(20), GoodLuckRate, 0),
        new(EventType.BlackjackInsurance, "🪙 ブラックジャック保険", "10分間、ブラックジャックのサレンダーでベットの80%を返還", TimeSpan.FromMinutes(10), 0, 0),
        new(EventType.DoubleUpBoost, "🔥 倍倍倍プッシュ！！", "10分間、DoubleUpの当選倍率を2.4倍に変更", TimeSpan.FromMinutes(10), 0, DoubleUpBoostCost),
        new(EventType.MinesSafetyOne, "💣 Mines安全週間１", "10分間、Minesの爆弾数を4個から3個に減少", TimeSpan.FromMinutes(10), MinesSafetyOneRate, 0),
        new(EventType.MinesSafetyTwo, "💣 Mines安全週間２", "10分間、Minesの爆弾数を4個から2個に減少", TimeSpan.FromMinutes(10), MinesSafetyTwoRate, 0),
        new(EventType.MinesSafetyThree, "💣 Mines安全週間３", "10分間、Minesの爆弾数を4個から1個に減少", TimeSpan.FromMinutes(10), MinesSafetyThreeRate, 0),
        new(EventType.LiveOrDie, "💎 生きるか死ぬか", "次の勝負1回のみ、勝敗の払い戻し予定額を5倍", TimeSpan.Zero, 0, 0, true)
    ];

    public ServerEvent? GetActiveEvent(ulong guildId)
    {
        if (!_activeEvents.TryGetValue(guildId, out var activeEvent))
            return null;
        if (activeEvent.IsActive(DateTimeOffset.UtcNow))
            return activeEvent;
        _activeEvents.TryRemove(guildId, out _);
        return null;
    }

    public EventDefinition GetDefinition(EventType type)
        => Definitions.First(definition => definition.Type == type);

    public Task<EventCost> CalculateCostAsync(ulong guildId, ulong userId, EventType type)
        => CalculateCostForUserAsync(guildId, userId, type);

    private async Task<EventCost> CalculateCostAsync(ulong guildId, EventType type)
    {
        var definition = GetDefinition(type);
        if (definition.FixedCost > 0)
            return new EventCost(definition.FixedCost, $"{definition.FixedCost:N0}コイン");
        if (definition.IsPersonal)
        {
            throw new InvalidOperationException("個人イベントのコスト計算には発動者IDが必要です。");
        }

        var ranking = await _coinService.GetTopRankingAsync(guildId, TopRankingLimit);
        var total = ranking.Sum(user => user.Coins);
        return new EventCost((long)Math.Floor(total * definition.TopRankingRate), $"TOP10総資産の{definition.TopRankingRate:P0}");
    }

    public async Task<ServerEvent> StartEventAsync(ulong guildId, ulong userId, EventType type)
    {
        var definition = GetDefinition(type);
        if (definition.IsPersonal && HasPersonalEvent(guildId, userId))
            throw new InvalidOperationException("生きるか死ぬかは、現在すでに発動中です。");
        var cost = await CalculateCostForUserAsync(guildId, userId, type);
        if (!await _coinService.CanAffordAsync(guildId, userId, cost.Amount))
            throw new InvalidOperationException("コインが不足しています。");
        if (!definition.IsPersonal && GetActiveEvent(guildId) != null)
            throw new InvalidOperationException($"現在「{GetDefinition(GetActiveEvent(guildId)!.Type).Name}」が発動中のため実行できません。");

        var now = DateTimeOffset.UtcNow;
        var started = new ServerEvent(guildId, type, userId, now, definition.IsPersonal ? DateTimeOffset.MaxValue : now.Add(definition.Duration), definition.IsPersonal);
        if (definition.IsPersonal)
        {
            if (!_personalEvents.TryAdd(CreatePersonalKey(guildId, userId), started))
                throw new InvalidOperationException("生きるか死ぬかは、現在すでに発動中です。");
        }
        else if (!_activeEvents.TryAdd(guildId, started))
        {
            throw new InvalidOperationException("別のサーバーイベントが発動しました。もう一度お試しください。");
        }

        try
        {
            if (_repository != null)
            {
                if (!await _repository.TryStartWithPaymentAsync(started, cost.Amount))
                    throw new InvalidOperationException("コインが不足しています。");
            }
            else
            {
                await _coinService.RemoveCoinsAsync(guildId, userId, cost.Amount);
            }
        }
        catch
        {
            if (definition.IsPersonal)
                _personalEvents.TryRemove(CreatePersonalKey(guildId, userId), out _);
            else
                _activeEvents.TryRemove(guildId, out _);
            throw;
        }

        if (_notification != null && !definition.IsPersonal)
            await _notification(started, true);
        return started;
    }

    public EventEffects GetEffects(ulong guildId, ulong userId)
    {
        var effects = new EventEffects();
        var active = GetActiveEvent(guildId);
        if (active != null)
            effects = ApplyServerEffects(active.Type, effects);
        if (_personalEvents.ContainsKey(CreatePersonalKey(guildId, userId)))
            effects = effects with { HasPersonalRisk = true, PayoutMultiplier = LiveOrDieMultiplier };
        return effects;
    }

    public long CalculatePayout(
        ulong guildId,
        ulong userId,
        long payout,
        string gameType,
        long nonMultiplierAmount = 0)
    {
        if (payout <= 0)
            return 0;
        nonMultiplierAmount = Math.Clamp(nonMultiplierAmount, 0, payout);
        var multiplier = HasPersonalEvent(guildId, userId) && gameType != "slot"
            ? LiveOrDieMultiplier
            : GetActiveEvent(guildId)?.Type switch
            {
                EventType.DoublePayout when gameType is "blackjack" or "doubleup" or "slot" or "roulette" => 1.5m,
                EventType.GoodLuck when gameType is "blackjack" or "slot" or "roulette" => 1.25m,
                _ => 1m
            };
        var multiplierTarget = payout - nonMultiplierAmount;
        return checked(nonMultiplierAmount + (long)Math.Floor(multiplierTarget * multiplier));
    }

    public long CalculateLossRefund(ulong guildId, ulong userId, long bet)
    {
        if (bet <= 0)
            return 0;
        var effects = GetEffects(guildId, userId);
        return (long)Math.Floor(bet * effects.LoseRefundRate);
    }

    public async Task<long> ResolvePersonalLossAsync(ulong guildId, ulong userId)
    {
        if (!HasPersonalEvent(guildId, userId))
            return 0;
        var balance = await _coinService.GetBalanceAsync(guildId, userId);
        var amount = (long)Math.Floor(balance * LiveOrDieRate);
        if (amount > 0)
            await _coinService.RemoveCoinsAsync(guildId, userId, amount);
        ConsumePersonalEvent(guildId, userId);
        if (amount <= 0)
            return 0;
        return amount;
    }

    public bool HasPersonalEvent(ulong guildId, ulong userId)
        => _personalEvents.ContainsKey(CreatePersonalKey(guildId, userId));

    public bool ConsumePersonalEvent(ulong guildId, ulong userId)
    {
        if (!_personalEvents.TryRemove(CreatePersonalKey(guildId, userId), out var personalEvent))
            return false;
        if (_repository != null)
            _ = _repository.DeleteAsync(personalEvent);
        return true;
    }

    public TimeSpan? GetRemainingTime(ulong guildId)
    {
        var active = GetActiveEvent(guildId);
        return active == null ? null : active.EndsAt - DateTimeOffset.UtcNow;
    }

    public void StartMonitoring()
    {
        _monitorTask ??= MonitorAsync();
    }

    private async Task MonitorAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            foreach (var pair in _activeEvents.ToArray())
            {
                if (pair.Value.IsActive(DateTimeOffset.UtcNow))
                    continue;
                if (_activeEvents.TryRemove(pair.Key, out var ended))
                {
                    if (_repository != null)
                        await _repository.DeleteAsync(ended);
                    if (_notification != null)
                        await _notification(ended, false);
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(30), _cancellation.Token);
        }
    }

    private async Task<EventCost> CalculateCostForUserAsync(ulong guildId, ulong userId, EventType type)
    {
        var definition = GetDefinition(type);
        if (definition.IsPersonal)
        {
            var balance = await _coinService.GetBalanceAsync(guildId, userId);
            return new EventCost(CalculatePercentageCost(balance, LiveOrDieRate), "発動者の所持金の50%");
        }
        if (type is EventType.BlackjackInsurance or EventType.MinesSafetyOne or EventType.MinesSafetyTwo or EventType.MinesSafetyThree)
        {
            var balance = await _coinService.GetBalanceAsync(guildId, userId);
            var rate = type == EventType.BlackjackInsurance
                ? BlackjackInsuranceRate
                : definition.TopRankingRate;
            return new EventCost(
                CalculatePercentageCost(balance, rate),
                $"発動者の所持金の{rate:P0}");
        }
        if (definition.FixedCost > 0)
            return new EventCost(definition.FixedCost, $"{definition.FixedCost:N0}コイン");
        return await CalculateCostAsync(guildId, type);
    }

    private static EventEffects ApplyServerEffects(EventType type, EventEffects effects)
        => type switch
        {
            EventType.DoublePayout => effects with { PayoutMultiplier = 1.5m },
            EventType.HotSlot => effects,
            EventType.LossBack => effects with { LoseRefundRate = 0.5m },
            EventType.GoodLuck => effects with { PayoutMultiplier = 1.25m },
            EventType.BlackjackInsurance => effects with { SurrenderRefundRate = BlackjackInsuranceRefundRate },
            EventType.DoubleUpBoost => effects with { DoubleUpMultiplier = 2.4m },
            EventType.MinesSafetyOne => effects with { MinesBombReduction = 1 },
            EventType.MinesSafetyTwo => effects with { MinesBombReduction = 2 },
            EventType.MinesSafetyThree => effects with { MinesBombReduction = 3 },
            _ => effects
        };

    private static string CreatePersonalKey(ulong guildId, ulong userId) => $"{guildId}:{userId}";

    private static long CalculatePercentageCost(long balance, decimal rate)
    {
        if (balance <= 0)
            return 0;
        return Math.Max(1, (long)Math.Floor(balance * rate));
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        if (_monitorTask != null)
        {
            try { await _monitorTask; } catch (OperationCanceledException) { }
        }
        _cancellation.Dispose();
    }
}
