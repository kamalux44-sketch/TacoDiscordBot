using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using TacoDiscordBot.Services.Interface;

namespace TacoDiscordBot.Services;

public sealed record RouletteConfiguration(
    IReadOnlyList<int> Wheel,
    IReadOnlyDictionary<int, long> PayoutMultipliers
)
{
    public static RouletteConfiguration Default { get; } = new(
        [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 3, 3, 3, 3, 3, 3, 5, 5, 5, 5, 10, 10, 20],
        new Dictionary<int, long> { [1] = 2, [3] = 4, [5] = 6, [10] = 11, [20] = 21 }
    );
}

public sealed class RouletteService
{
    private static readonly TimeSpan[] SpinDelays =
    [
        TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(220), TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(400),
        TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(700)
    ];

    private static readonly string[] SpinFrames =
    [
        "             #########\n         ####    20   ####\n      ### 1             1 ###\n    ## 5                   3 ##\n   # 3                       5 #\n  # 1                           #\n #                             1 #\n#                ↑               #\n# 10             ◇             5 #\n#                                 #\n #                             3 #\n  # 1                           #\n   #  3                      1 #\n    ##   1                10 ##\n      ###  5            1 ###\n         ####  1    3 ####\n             #########",
        "             #########\n         ####    20   ####\n      ### 1             1 ###\n    ## 5                   3 ##\n   # 3                       5 #\n  # 1                           #\n #                             1 #\n#                                 #\n# 10             ◇→           5 #\n#                                 #\n #                             3 #\n  # 1                           #\n   #  3                      1 #\n    ##   1                10 ##\n      ###  5            1 ###\n         ####  1    3 ####\n             #########",
        "             #########\n         ####    20   ####\n      ### 1             1 ###\n    ## 5                   3 ##\n   # 3                       5 #\n  # 1                           #\n #                             1 #\n#                                 #\n# 10             ◇             5 #\n#                ↓               #\n #                             3 #\n  # 1                           #\n   #  3                      1 #\n    ##   1                10 ##\n      ###  5            1 ###\n         ####  1    3 ####\n             #########",
        "             #########\n         ####    20   ####\n      ### 1             1 ###\n    ## 5                   3 ##\n   # 3                       5 #\n  # 1                           #\n #                             1 #\n#                                 #\n# 10           ←◇             5 #\n#                                 #\n #                             3 #\n  # 1                           #\n   #  3                      1 #\n    ##   1                10 ##\n      ###  5            1 ###\n         ####  1    3 ####\n             #########"
        ,
        "             #########\n         ####    20   ####\n      ### 1             1 ###\n    ## 5                   3 ##\n   # 3                       5 #\n  # 1                           #\n #                             1 #\n#                ↑               #\n# 10             ◇             5 #\n#                                 #\n #                             3 #\n  # 1                           #\n   #  3                      1 #\n    ##   1                10 ##\n      ###  5            1 ###\n         ####  1    3 ####\n             #########",
        "             #########\n         ####    20   ####\n      ### 1             1 ###\n    ## 5                   3 ##\n   # 3                       5 #\n  # 1                           #\n #                             1 #\n#                                 #\n# 10             ◇→           5 #\n#                                 #\n #                             3 #\n  # 1                           #\n   #  3                      1 #\n    ##   1                10 ##\n      ###  5            1 ###\n         ####  1    3 ####\n             #########",
        "             #########\n         ####    20   ####\n      ### 1             1 ###\n    ## 5                   3 ##\n   # 3                       5 #\n  # 1                           #\n #                             1 #\n#                                 #\n# 10             ◇             5 #\n#                ↓               #\n #                             3 #\n  # 1                           #\n   #  3                      1 #\n    ##   1                10 ##\n      ###  5            1 ###\n         ####  1    3 ####\n             #########",
        "             #########\n         ####    20   ####\n      ### 1             1 ###\n    ## 5                   3 ##\n   # 3                       5 #\n  # 1                           #\n #                             1 #\n#                                 #\n# 10           ←◇             5 #\n#                                 #\n #                             3 #\n  # 1                           #\n   #  3                      1 #\n    ##   1                10 ##\n      ###  5            1 ###\n         ####  1    3 ####\n             #########"
    ];

    private readonly ICoinService _coinService;
    private readonly EventManager? _eventManager;
    private readonly RouletteConfiguration _configuration;
    private readonly ConcurrentDictionary<string, RouletteGame> _games = new();

    public RouletteService(
        ICoinService coinService,
        RouletteConfiguration? configuration = null,
        EventManager? eventManager = null
    )
    {
        _coinService = coinService ?? throw new ArgumentNullException(nameof(coinService));
        _configuration = configuration ?? RouletteConfiguration.Default;
        _eventManager = eventManager;
        ValidateConfiguration(_configuration);
    }

    public RouletteConfiguration Configuration => _configuration;

    public async Task StartAsync(ulong guildId, ulong userId, long bet)
    {
        ValidateBet(bet);
        var key = CreateGameKey(guildId, userId);
        if (_games.ContainsKey(key))
            throw new InvalidOperationException("⚠️ 現在ルーレットをプレイ中です。先に現在のゲームを終了してください。");

        await _coinService.RemoveCoinsAsync(guildId, userId, bet);
        if (!_games.TryAdd(key, new RouletteGame(guildId, userId, bet)))
        {
            await _coinService.AddCoinsAsync(guildId, userId, bet);
            throw new InvalidOperationException("⚠️ 現在ルーレットをプレイ中です。");
        }
    }

    public async Task<RouletteResult> SelectAsync(
        ulong guildId,
        ulong userId,
        int prediction,
        Func<DiscordEmbed, Task>? onSpinFrame = null
    )
    {
        if (!_configuration.PayoutMultipliers.ContainsKey(prediction))
            throw new ArgumentOutOfRangeException(nameof(prediction), "賭ける数字は1、3、5、10、20から選択してください。");

        if (!_games.TryGetValue(CreateGameKey(guildId, userId), out var game))
            throw new InvalidOperationException("このルーレットは終了しています。");

        lock (game)
        {
            if (game.IsRunning)
                throw new InvalidOperationException("⚠️ ルーレットはすでに回転中です。");
            game.IsRunning = true;
        }

        var result = _configuration.Wheel[Random.Shared.Next(_configuration.Wheel.Count)];
        var isWin = result == prediction;
        var payout = CalculatePayout(game.Bet, prediction, result, _configuration.PayoutMultipliers);
        if (_eventManager != null)
        {
            payout = _eventManager.CalculatePayout(guildId, userId, payout, "roulette");
            if (payout == 0 && !isWin)
                payout = _eventManager.CalculateLossRefund(guildId, userId, game.Bet);
        }

        // 抽選結果を確定した後、編集通知で回転演出を行います。
        if (onSpinFrame != null)
        {
            for (var index = 0; index < SpinFrames.Length; index++)
            {
                await onSpinFrame(CreateSpinEmbed(SpinFrames[index]));
                await Task.Delay(SpinDelays[index]);
            }
        }

        _games.TryRemove(CreateGameKey(guildId, userId), out _);
        if (payout > 0)
            await _coinService.AddCoinsAsync(guildId, userId, payout);

        if (_eventManager != null)
        {
            if (!isWin)
                await _eventManager.ResolvePersonalLossAsync(guildId, userId);
            else
                _eventManager.ConsumePersonalEvent(guildId, userId);
        }

        var displayMultiplier = payout > 0
            ? (decimal)payout / game.Bet
            : _configuration.PayoutMultipliers[prediction];
        return new RouletteResult(prediction, result, game.Bet, payout, isWin, displayMultiplier);
    }

    public static long CalculatePayout(long bet, int prediction, int result, IReadOnlyDictionary<int, long> multipliers)
    {
        if (bet <= 0 || !multipliers.TryGetValue(prediction, out var multiplier) || prediction != result)
            return 0;
        return checked(bet * multiplier);
    }

    private static DiscordEmbed CreateSpinEmbed(string frame)
        => new DiscordEmbedBuilder()
            .WithTitle("🎲 ルーレット回転中…")
            .WithDescription($"```\n{frame}\n```")
            .WithColor(DiscordColor.Blurple)
            .Build();

    private static void ValidateBet(long bet)
    {
        if (bet <= 0)
            throw new ArgumentOutOfRangeException(nameof(bet), "ベットは1以上で指定してください。");
    }

    private static void ValidateConfiguration(RouletteConfiguration configuration)
    {
        if (configuration.Wheel.Count == 0 || configuration.Wheel.Any(value => !configuration.PayoutMultipliers.ContainsKey(value)))
            throw new ArgumentException("ルーレット設定のホイールまたは倍率が不正です。", nameof(configuration));
    }

    private static string CreateGameKey(ulong guildId, ulong userId) => $"{guildId}:{userId}";

    private sealed class RouletteGame(ulong guildId, ulong userId, long bet)
    {
        public ulong GuildId { get; } = guildId;
        public ulong UserId { get; } = userId;
        public long Bet { get; } = bet;
        public bool IsRunning { get; set; }
    }
}

public sealed record RouletteResult(
    int Prediction,
    int Result,
    long Bet,
    long Payout,
    bool IsWin,
    decimal PayoutMultiplier
)
{
    public DiscordEmbed Embed => new DiscordEmbedBuilder()
        .WithTitle(IsWin ? $"🎉 ルーレット結果: {Result}" : $"🎲 ルーレット結果: {Result}")
        .WithDescription(IsWin
            ? $"あなたの予想: {Prediction}\n賭け金: {Bet:N0} Scrap\n\n的中！\n\n配当倍率: ×{PayoutMultiplier}\n獲得: {Payout:N0} Scrap"
            : $"あなたの予想: {Prediction}\n賭け金: {Bet:N0} Scrap\n\nハズレ…\n\n獲得: 0 Scrap")
        .WithColor(IsWin ? DiscordColor.Green : DiscordColor.Red)
        .Build();
}
