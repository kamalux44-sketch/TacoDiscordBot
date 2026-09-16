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
    private const int MaximumSafeCount = MinesGame.BoardSize - MinesGame.BombCount;
    private readonly ICoinService _coinService;
    private readonly ConcurrentDictionary<string, MinesGame> _games = new();
    private readonly Func<IReadOnlyCollection<int>> _createBombs;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public MinesService(
        ICoinService coinService,
        Func<IReadOnlyCollection<int>>? createBombs = null
    )
    {
        _coinService = coinService ?? throw new ArgumentNullException(nameof(coinService));
        _createBombs = createBombs ?? CreateRandomBombs;
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
        var game = new MinesGame(guildId, userId, bet, _createBombs());
        if (game.Bombs.Count != MinesGame.BombCount || game.Bombs.Any(index => index < 0 || index >= MinesGame.BoardSize))
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

        if (game.Bombs.Contains(index))
        {
            game.State = MinesGameState.Lost;
            _games.TryRemove(CreateGameKey(guildId, userId), out _);
            return CreateResult(game, "💥 GAME OVER");
        }

        if (game.SafeOpenedCount == MaximumSafeCount)
        {
            game.State = MinesGameState.Won;
            await _coinService.AddCoinsAsync(guildId, userId, game.CurrentAmount);
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
        await _coinService.AddCoinsAsync(guildId, userId, game.CurrentAmount);
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

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _gate;

        public Releaser(SemaphoreSlim gate) => _gate = gate;

        public void Dispose() => _gate.Release();
    }
}
