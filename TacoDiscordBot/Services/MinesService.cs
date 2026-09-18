using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services.Interface;

namespace TacoDiscordBot.Services;

public sealed class MinesService
{
    private const int MinimumBet = 1;
    private readonly ICoinService _coinService;
    private readonly ConcurrentDictionary<string, MinesGame> _games = new();
    private readonly Func<IReadOnlyCollection<int>> _createBombs;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly RoleService _roleService;
    private readonly EventManager _eventManager;

    public MinesService(
        ICoinService coinService,
        Func<IReadOnlyCollection<int>>? createBombs = null,
        RoleService? roleService = null,
        EventManager? eventManager = null
    )
    {
        _coinService = coinService ?? throw new ArgumentNullException(nameof(coinService));
        _createBombs = createBombs ?? CreateRandomBombs;
        _roleService = roleService;
        _eventManager = eventManager;
    }

    public async Task<MinesResult> StartAsync(ulong guildId, ulong userId, long bet)
    {
        if (bet < MinimumBet)
            throw new ArgumentOutOfRangeException(nameof(bet), "掛け金は1以上で指定してください。");

        using var gameLock = await EnterAsync(guildId, userId);
        var key = CreateGameKey(guildId, userId);
        if (_games.ContainsKey(key))
            throw new InvalidOperationException("⚠️ 現在 MINES をプレイ中です。先に現在のゲームを終了してください。");

        await _coinService.RemoveCoinsAsync(guildId, userId, bet);
        var reduction = _eventManager?.GetMinesBombReduction(guildId, userId) ?? 0;
        var game = new MinesGame(guildId, userId, bet, CreateBombs(reduction));
        if (game.Bombs.Count != MinesGame.BombCount - reduction || game.Bombs.Any(index => index < 0 || index >= MinesGame.BoardSize))
        {
            await _coinService.AddCoinsAsync(guildId, userId, bet);
            throw new InvalidOperationException("爆弾配置を作成できませんでした。");
        }

        if (!_games.TryAdd(key, game))
        {
            await _coinService.AddCoinsAsync(guildId, userId, bet);
            throw new InvalidOperationException("⚠️ 現在 MINES をプレイ中です。先に現在のゲームを終了してください。");
        }

        return CreateResult(game, "マスを選択するか、回収してください。");
    }

    public async Task<bool> CancelAndRefundAsync(ulong guildId, ulong userId, long bet)
    {
        using var gameLock = await EnterAsync(guildId, userId);
        if (!_games.TryRemove(CreateGameKey(guildId, userId), out _))
            return false;

        await _coinService.AddCoinsAsync(guildId, userId, bet);
        return true;
    }

    public async Task<MinesResult> OpenAsync(ulong guildId, ulong userId, int index)
    {
        if (index < 0 || index >= MinesGame.BoardSize)
            throw new ArgumentOutOfRangeException(nameof(index));

        using var gameLock = await EnterAsync(guildId, userId);
        var game = GetGame(guildId, userId);
        if (game.State != MinesGameState.Playing)
            throw new InvalidOperationException("このMINESゲームは終了しています。");
        if (!game.Opened.Add(index))
            throw new InvalidOperationException("そのマスはすでに開放されています。");

        if (_roleService != null && game.SafeOpenedCount > 0)
            await _roleService.RecordEventAsync(
                guildId,
                userId,
                "mines_safe_count",
                game.SafeOpenedCount,
                updateRoles: false
            );

        if (game.Bombs.Contains(index))
        {
            game.State = MinesGameState.Lost;
            _games.TryRemove(CreateGameKey(guildId, userId), out _);
            if (_roleService != null && game.SafeOpenedCount == 0)
                await _roleService.RefreshUserRolesAsync(guildId, userId);
            var refund = _eventManager?.CalculateLossRefund(guildId, userId, game.Bet) ?? 0;
            if (refund > 0)
                await _coinService.AddCoinsAsync(guildId, userId, refund);
            if (_eventManager != null)
                await _eventManager.ResolvePersonalLossAsync(guildId, userId);
            return CreateResult(game, "💥 GAME OVER");
        }

        if (game.SafeOpenedCount == MinesGame.BoardSize - game.Bombs.Count)
        {
            game.State = MinesGameState.Won;
            var payout = _eventManager?.CalculatePayout(guildId, userId, game.CurrentAmount, "mines")
                ?? game.CurrentAmount;
            await _coinService.AddCoinsAsync(guildId, userId, payout);
            _eventManager?.ConsumePersonalEvent(guildId, userId);
            _games.TryRemove(CreateGameKey(guildId, userId), out _);
            return CreateResult(game, "🎉 ALL SAFE！自動回収しました。");
        }

        return CreateResult(game, "安全マスです。続けるか回収してください。");
    }

    public async Task<MinesResult> CashOutAsync(ulong guildId, ulong userId)
    {
        using var gameLock = await EnterAsync(guildId, userId);
        var game = GetGame(guildId, userId);
        if (game.State != MinesGameState.Playing)
            throw new InvalidOperationException("このMINESゲームは終了しています。");

        game.State = MinesGameState.CashedOut;
        var payout = _eventManager?.CalculatePayout(guildId, userId, game.CurrentAmount, "mines")
            ?? game.CurrentAmount;
        await _coinService.AddCoinsAsync(guildId, userId, payout);
        _eventManager?.ConsumePersonalEvent(guildId, userId);
        _games.TryRemove(CreateGameKey(guildId, userId), out _);
        return CreateResult(game, "💰 CHECKOUT！");
    }

    private MinesGame GetGame(ulong guildId, ulong userId)
        => _games.TryGetValue(CreateGameKey(guildId, userId), out var game)
            ? game
            : throw new InvalidOperationException("このMINESゲームは終了しています。");

    private static string CreateGameKey(ulong guildId, ulong userId)
        => $"{guildId}:{userId}";

    private async Task<IDisposable> EnterAsync(ulong guildId, ulong userId)
    {
        var gate = _locks.GetOrAdd($"{guildId}:{userId}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        return new Releaser(gate);
    }

    private static MinesResult CreateResult(MinesGame game, string message)
        => new(game, message);

    private static IReadOnlyCollection<int> CreateRandomBombs()
        => Enumerable.Range(0, MinesGame.BoardSize)
            .OrderBy(_ => Random.Shared.Next())
            .Take(MinesGame.BombCount)
            .ToArray();

    private IReadOnlyCollection<int> CreateBombs(int reduction)
    {
        if (reduction <= 0)
            return _createBombs();
        return Enumerable.Range(0, MinesGame.BoardSize)
            .OrderBy(_ => Random.Shared.Next())
            .Take(MinesGame.BombCount - reduction)
            .ToArray();
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _gate;

        public Releaser(SemaphoreSlim gate) => _gate = gate;

        public void Dispose() => _gate.Release();
    }
}
