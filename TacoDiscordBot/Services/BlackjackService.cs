using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services.Interface;

namespace TacoDiscordBot.Services;

public enum BlackjackOutcome { Loss, Push, Win, Blackjack, Surrender, DealerBlackjack }

public sealed class BlackjackService
{
    private readonly ICoinService _coinService;
    private readonly RoleService _roleService;
    private readonly EventManager _eventManager;
    private readonly ConcurrentDictionary<string, BlackjackGame> _games = new();

    public BlackjackService(
        ICoinService coinService,
        RoleService? roleService = null,
        EventManager? eventManager = null
    )
    {
        _coinService = coinService ?? throw new ArgumentNullException(nameof(coinService));
        _roleService = roleService;
        _eventManager = eventManager;
    }

    public async Task<BlackjackResult> StartAsync(ulong guildId, ulong userId, long bet)
    {
        ValidateBet(bet);
        var gameKey = CreateGameKey(guildId, userId);
        if (_games.ContainsKey(gameKey))
            throw new InvalidOperationException("⚠️ 現在ブラックジャックをプレイ中です。\n先に現在のゲームを終了してください。");

        await _coinService.RemoveCoinsAsync(guildId, userId, bet);
        var deck = CreateShuffledDeck();
        var playerCards = new[] { deck.Dequeue(), deck.Dequeue() };
        var dealerCards = new[] { deck.Dequeue(), deck.Dequeue() };
        var game = new BlackjackGame(guildId, userId, bet, playerCards, dealerCards, deck);

        if (!_games.TryAdd(gameKey, game))
        {
            await _coinService.AddCoinsAsync(guildId, userId, bet);
            throw new InvalidOperationException("⚠️ 現在ブラックジャックをプレイ中です。");
        }

        if (CalculateTotal(game.PlayerCards) == 21)
            return await FinishAsync(game, BlackjackOutcome.Blackjack);

        return CreateResult(game, false, "あなたのターンです。", 0);
    }

    public async Task<BlackjackResult> HitAsync(ulong guildId, ulong userId)
    {
        if (!_games.TryGetValue(CreateGameKey(guildId, userId), out var game))
            throw new InvalidOperationException("このブラックジャックゲームは終了しています。");

        lock (game)
        {
            if (game.IsFinished)
                throw new InvalidOperationException("このブラックジャックゲームは終了しています。");
            game.PlayerCards.Add(game.Deck.Dequeue());
        }

        var total = CalculateTotal(game.PlayerCards);
        if (total > 21)
            return await FinishAsync(game, BlackjackOutcome.Loss);

        return CreateResult(game, false, "もう一度カードを引くか、STANDしてください。", 0);
    }

    public async Task<BlackjackResult> SurrenderAsync(ulong guildId, ulong userId)
    {
        if (!_games.TryGetValue(CreateGameKey(guildId, userId), out var game))
            throw new InvalidOperationException("このブラックジャックゲームは終了しています。");

        lock (game)
        {
            if (game.IsFinished)
                throw new InvalidOperationException("このブラックジャックゲームは終了しています。");
        }

        // ディーラーの初期2枚が21の場合は、サレンダーできません。
        if (CalculateTotal(game.DealerCards) == 21)
            return await FinishAsync(game, BlackjackOutcome.DealerBlackjack);

        return await FinishAsync(game, BlackjackOutcome.Surrender);
    }

    public async Task<BlackjackResult> StandAsync(
        ulong guildId,
        ulong userId,
        Func<BlackjackResult, Task>? onDealerCardRevealed = null
    )
    {
        if (!_games.TryGetValue(CreateGameKey(guildId, userId), out var game))
            throw new InvalidOperationException("このブラックジャックゲームは終了しています。");

        var revealResults = new List<BlackjackResult>();
        lock (game)
        {
            if (game.IsFinished)
                throw new InvalidOperationException("このブラックジャックゲームは終了しています。");

            revealResults.Add(CreateResult(game, false, "ディーラーのカードを公開します。", 0, true));
            while (CalculateTotal(game.DealerCards) < 17)
            {
                game.DealerCards.Add(game.Deck.Dequeue());
                revealResults.Add(CreateResult(game, false, "ディーラーがカードを引きました。", 0, true));
            }
        }

        if (onDealerCardRevealed != null)
        {
            foreach (var revealResult in revealResults)
                await onDealerCardRevealed(revealResult);
        }

        var playerTotal = CalculateTotal(game.PlayerCards);
        var dealerTotal = CalculateTotal(game.DealerCards);
        var outcome = playerTotal > 21
            ? BlackjackOutcome.Loss
            : dealerTotal > 21 || playerTotal > dealerTotal
                ? BlackjackOutcome.Win
                : playerTotal == dealerTotal ? BlackjackOutcome.Push : BlackjackOutcome.Loss;
        return await FinishAsync(game, outcome);
    }

    public static int CalculateTotal(IReadOnlyList<BlackjackCard> cards)
    {
        var total = cards.Sum(card => card.BaseValue);
        var aces = cards.Count(card => card.Rank == "A");
        while (total > 21 && aces-- > 0)
            total -= 10;
        return total;
    }

    public static long CalculateSurrenderPayout(long bet) => bet / 2;

    public static long CalculateScheduledPayout(long bet, BlackjackOutcome outcome)
        => outcome switch
        {
            BlackjackOutcome.Blackjack => checked(bet * 3),
            BlackjackOutcome.Win => checked(bet * 2),
            BlackjackOutcome.Loss => checked(bet * 2),
            _ => 0
        };

    private async Task<BlackjackResult> FinishAsync(BlackjackGame game, BlackjackOutcome outcome)
    {
        lock (game)
        {
            if (game.IsFinished)
                throw new InvalidOperationException("このブラックジャックゲームは終了しています。");
            game.IsFinished = true;
        }

        _games.TryRemove(CreateGameKey(game.GuildId, game.UserId), out _);
        var payout = outcome switch
        {
            BlackjackOutcome.Blackjack => CalculateScheduledPayout(game.Bet, outcome),
            BlackjackOutcome.Win => CalculateScheduledPayout(game.Bet, outcome),
            BlackjackOutcome.Push => game.Bet,
            BlackjackOutcome.Surrender => CalculateSurrenderPayout(game.Bet),
            _ => 0
        };
        if (_eventManager != null)
        {
            payout = _eventManager.CalculatePayout(game.GuildId, game.UserId, payout, "blackjack");
            if (payout == 0 && outcome == BlackjackOutcome.Loss)
                payout = _eventManager.CalculateLossRefund(game.GuildId, game.UserId, game.Bet);
            if (outcome == BlackjackOutcome.Surrender)
            {
                var effects = _eventManager.GetEffects(game.GuildId, game.UserId);
                if (_eventManager.HasPersonalEvent(game.GuildId, game.UserId))
                {
                    payout = 0;
                    await _eventManager.ResolvePersonalLossAsync(
                        game.GuildId,
                        game.UserId,
                        0);
                }
                else if (effects.SurrenderRefundRate > 0)
                    payout = (long)Math.Floor(game.Bet * effects.SurrenderRefundRate);
            }
            if (outcome == BlackjackOutcome.Loss)
                await _eventManager.ResolvePersonalLossAsync(
                    game.GuildId,
                    game.UserId,
                    CalculateScheduledPayout(game.Bet, outcome));
            else
                _eventManager.ConsumePersonalEvent(game.GuildId, game.UserId);
        }
        if (payout > 0)
            await _coinService.AddCoinsAsync(game.GuildId, game.UserId, payout);

        if (_roleService != null)
        {
            var conditionType = outcome is BlackjackOutcome.Win or BlackjackOutcome.Blackjack
                ? "blackjack_win_streak"
                : outcome == BlackjackOutcome.Loss ? "blackjack_loss_streak" : null;
            if (conditionType != null)
                await _roleService.RecordEventAsync(game.GuildId, game.UserId, conditionType);
        }

        var message = outcome switch
        {
            BlackjackOutcome.Blackjack => "🎉 BLACKJACK! 3倍払い戻し",
            BlackjackOutcome.Win => "🎉 勝利！",
            BlackjackOutcome.Push => "🤝 PUSH（引き分け）",
            BlackjackOutcome.Surrender => "🏳️ サレンダー（ベットの半額返金）",
            BlackjackOutcome.DealerBlackjack => "💥 ディーラーがブラックジャックのためサレンダーできません。",
            _ => CalculateTotal(game.PlayerCards) > 21 ? "💥 BUST!" : "😢 負け"
        };
        return CreateResult(game, true, message, payout);
    }

    private static string CreateGameKey(ulong guildId, ulong userId) => $"{guildId}:{userId}";

    private static BlackjackResult CreateResult(
        BlackjackGame game,
        bool finished,
        string message,
        long payout,
        bool revealDealerCards = false
    )
    {
        var dealer = finished || revealDealerCards
            ? string.Join(" ", game.DealerCards)
            : $"🂠 {game.DealerCards[1]}";
        var embed = new DiscordEmbedBuilder()
            .WithTitle("🃏 BLACKJACK")
            .WithDescription($"**あなた**\n{string.Join(" ", game.PlayerCards)}\n合計: {CalculateTotal(game.PlayerCards)}\n\n**ディーラー**\n{dealer}\n合計: {(finished || revealDealerCards ? CalculateTotal(game.DealerCards).ToString() : "❓")}\n\n{message}\nベット: {game.Bet:N0}\n払い戻し: {payout:N0}")
            .WithColor(finished ? DiscordColor.Green : DiscordColor.Blurple)
            .Build();
        return new BlackjackResult(embed, finished);
    }

    private static void ValidateBet(long bet)
    {
        if (bet <= 0)
            throw new ArgumentOutOfRangeException(nameof(bet), "ベットは1以上で指定してください。");
    }

    private static Queue<BlackjackCard> CreateShuffledDeck()
    {
        var suits = new[] { "♠", "♥", "♦", "♣" };
        var ranks = new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };
        var cards = suits.SelectMany(suit => ranks.Select(rank => new BlackjackCard(rank, suit))).ToList();
        for (var i = cards.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
        return new Queue<BlackjackCard>(cards);
    }

}

public sealed record BlackjackResult(DiscordEmbed Embed, bool IsFinished);
