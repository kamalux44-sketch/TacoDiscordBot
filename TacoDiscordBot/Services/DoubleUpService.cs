using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services.Interface;

namespace TacoDiscordBot.Services;

public sealed class DoubleUpService
{
    private const int MinimumCardNumber = 1;
    private const int MaximumCardNumber = 13;
    private const int SpecialCardNumber = 7;
    private const int MaximumMultiplier = 128;
    private const int DefaultRevealDelayMilliseconds = 800;
    private readonly ICoinService _coinService;
    private readonly ConcurrentDictionary<string, DoubleUpGame> _games = new();
    private readonly Func<int> _drawNumber;
    private readonly Func<TimeSpan, Task> _delay;

    public DoubleUpService(
        ICoinService coinService,
        Func<int>? drawNumber = null,
        Func<TimeSpan, Task>? delay = null
    )
    {
        _coinService = coinService ?? throw new ArgumentNullException(nameof(coinService));
        _drawNumber = drawNumber ?? (() => Random.Shared.Next(MinimumCardNumber, MaximumCardNumber + 1));
        _delay = delay ?? Task.Delay;
    }

    public async Task<DoubleUpResult> StartAsync(ulong guildId, ulong userId, long bet)
    {
        ValidateBet(bet);
        var key = CreateGameKey(guildId, userId);
        if (_games.ContainsKey(key))
            throw new InvalidOperationException("⚠️ 現在 DOUBLE UP をプレイ中です。\n先に現在のゲームを終了してください。");

        await _coinService.RemoveCoinsAsync(guildId, userId, bet);
        var game = new DoubleUpGame(guildId, userId, bet);
        if (!_games.TryAdd(key, game))
        {
            await _coinService.AddCoinsAsync(guildId, userId, bet);
            throw new InvalidOperationException("⚠️ 現在 DOUBLE UP をプレイ中です。");
        }

        return CreateResult(game, "HIGH / LOW を選択してください。");
    }

    public async Task<DoubleUpResult> SelectAsync(
        ulong guildId,
        ulong userId,
        DoubleUpChoice choice,
        Func<Task>? onDrawing = null
    )
    {
        var game = GetGame(guildId, userId);
        lock (game)
        {
            if (game.State != DoubleUpState.Selecting && game.State != DoubleUpState.Won)
                throw new InvalidOperationException("この DOUBLE UP ゲームは操作できません。");
            if (game.CurrentAmount >= checked(game.InitialBet * MaximumMultiplier))
                throw new InvalidOperationException("最大倍率に到達しています。CASH OUT を選択してください。");
            game.State = DoubleUpState.Processing;
            game.LastChoice = choice;
        }

        if (onDrawing != null)
            await onDrawing();
        await _delay(TimeSpan.FromMilliseconds(DefaultRevealDelayMilliseconds));

        var number = _drawNumber();
        if (number < MinimumCardNumber || number > MaximumCardNumber)
            throw new InvalidOperationException("抽選結果が不正です。");

        lock (game)
        {
            game.LastNumber = number;
            if (number == SpecialCardNumber)
            {
                game.SevenStreak++;
                game.State = DoubleUpState.Selecting;
                return CreateResult(game, CreateSevenMessage(game.SevenStreak));
            }

            game.SevenStreak = 0;
            var resultChoice = number >= 8 ? DoubleUpChoice.High : DoubleUpChoice.Low;
            if (resultChoice != choice)
            {
                game.CurrentAmount = 0;
                game.State = DoubleUpState.Lost;
                _games.TryRemove(CreateGameKey(guildId, userId), out _);
                return CreateResult(game, "💀 LOSE");
            }

            game.CurrentAmount = checked(game.CurrentAmount * 2);
            game.State = DoubleUpState.Won;
            return CreateResult(game, "🎉 WIN!");
        }
    }

    public async Task<DoubleUpResult> CashOutAsync(ulong guildId, ulong userId)
    {
        var game = GetGame(guildId, userId);
        lock (game)
        {
            if (game.State != DoubleUpState.Won)
                throw new InvalidOperationException("勝利後のみ CASH OUT を選択できます。");
            game.State = DoubleUpState.CashedOut;
        }

        await _coinService.AddCoinsAsync(guildId, userId, game.CurrentAmount);
        _games.TryRemove(CreateGameKey(guildId, userId), out _);
        return CreateResult(game, $"💰 CASH OUT\n{game.CurrentAmount:N0} を受け取りました。");
    }

    private DoubleUpGame GetGame(ulong guildId, ulong userId)
        => _games.TryGetValue(CreateGameKey(guildId, userId), out var game)
            ? game
            : throw new InvalidOperationException("この DOUBLE UP ゲームは終了しています。");

    private static DoubleUpResult CreateResult(DoubleUpGame game, string message)
    {
        if (!game.LastNumber.HasValue)
            return CreateStartResult(game);

        var choice = game.LastChoice switch
        {
            DoubleUpChoice.High => "🔺 HIGH",
            DoubleUpChoice.Low => "🔻 LOW",
            _ => string.Empty
        };
        var card = game.LastNumber.HasValue ? $"🃏 {game.LastNumber}" : string.Empty;
        var streak = game.SevenStreak >= 3
            ? $"🔥 SEVEN × {game.SevenStreak} 🔥\nLUCKY GOD MODE"
            : game.SevenStreak >= 2 ? $"⚡ SEVEN × {game.SevenStreak} ⚡" : string.Empty;
        var resultMessage = game.State switch
        {
            DoubleUpState.Lost => "💥💀 LOSE 💀💥\n\n残念！カードの数字が予想と違いました。\nこのゲームは終了です。",
            DoubleUpState.Won => "🎉✨ WIN! ✨🎉\n\n💰 賞金が2倍になりました！",
            DoubleUpState.CashedOut => $"🎊 CASH OUT! 🎊\n\n💰 {game.CurrentAmount:N0} を残高に追加しました。",
            _ => message
        };
        var description = string.Join("\n", new[]
        {
            "━━━━━━━━━━━━━━",
            choice,
            "",
            card,
            "",
            string.IsNullOrWhiteSpace(streak) ? null : streak,
            "",
            resultMessage,
            game.State == DoubleUpState.Selecting && game.LastNumber == SpecialCardNumber
                ? "賭け金はそのまま。\nもう一度チャレンジできます。\n\n🤔 次のカードは7より...?"
                : game.State == DoubleUpState.Won
                    ? "\n🔥 さらに倍を狙う？\nそれとも賞金を受け取る？"
                : null,
            "",
            $"💰 賞金: {game.CurrentAmount:N0}",
            "━━━━━━━━━━━━━━"
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var embed = new DiscordEmbedBuilder()
            .WithTitle("🎰 DOUBLE UP")
            .WithDescription(description)
            .WithColor(game.State switch
            {
                DoubleUpState.Lost => DiscordColor.Red,
                DoubleUpState.Won => DiscordColor.Green,
                DoubleUpState.CashedOut => DiscordColor.Gold,
                _ => DiscordColor.Blurple
            })
            .Build();
        return new DoubleUpResult(
            embed,
            game.State,
            game.CurrentAmount,
            game.LastNumber,
            game.LastChoice,
            game.CurrentAmount >= checked(game.InitialBet * MaximumMultiplier)
        );
    }

    private static DoubleUpResult CreateStartResult(DoubleUpGame game)
    {
        var description = string.Join("\n", new[]
        {
            "━━━━━━━━━━━━━━",
            "",
            "🂠",
            "",
            "🤔 さて、このカードは7より...?",
            "\n🔺 HIGH or🔻 LOW を選択してください。",
            "",
            $"💰 掛け金: {game.CurrentAmount:N0}",
            "",
            "━━━━━━━━━━━━━━"
        });
        var embed = new DiscordEmbedBuilder()
            .WithTitle("🎰 DOUBLE UP")
            .WithDescription(description)
            .WithColor(DiscordColor.Blurple)
            .Build();
        return new DoubleUpResult(
            embed,
            game.State,
            game.CurrentAmount,
            game.LastNumber,
            game.LastChoice,
            false
        );
    }

    private static string CreateSevenMessage(int streak)
        => streak >= 3 ? $"🔥 SEVEN × {streak} 🔥\nLUCKY GOD MODE" : streak >= 2 ? $"⚡ SEVEN × {streak} ⚡" : "⚡ SEVEN! ⚡";

    private static string CreateGameKey(ulong guildId, ulong userId) => $"{guildId}:{userId}";

    private static void ValidateBet(long bet)
    {
        if (bet <= 0)
            throw new ArgumentOutOfRangeException(nameof(bet), "ベットは1以上で指定してください。");
    }
}

public sealed record DoubleUpResult(
    DiscordEmbed Embed,
    DoubleUpState State,
    long CurrentAmount,
    int? Number,
    DoubleUpChoice? Choice,
    bool IsMaxMultiplier
)
{
    public bool IsFinished => State is DoubleUpState.Lost or DoubleUpState.CashedOut;

    public bool CanCashOut => State == DoubleUpState.Won;

    public bool CanSelect => State == DoubleUpState.Selecting || State == DoubleUpState.Won;

}
