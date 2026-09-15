using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services.Interface;

namespace TacoDiscordBot.Services;

public sealed class BlackjackService
{
    private readonly ICoinService _coinService;
    private readonly ConcurrentDictionary<ulong, BlackjackGame> _games = new();

    public BlackjackService(ICoinService coinService)
    {
        _coinService = coinService ?? throw new ArgumentNullException(nameof(coinService));
    }

    public async Task<BlackjackResult> StartAsync(ulong userId, long bet)
    {
        ValidateBet(bet);
        if (_games.ContainsKey(userId))
            throw new InvalidOperationException("⚠️ 現在ブラックジャックをプレイ中です。\n先に現在のゲームを終了してください。");

        await _coinService.RemoveCoinsAsync(userId, bet);
        var deck = CreateShuffledDeck();
        var playerCards = new[] { deck.Dequeue(), deck.Dequeue() };
        var dealerCards = new[] { deck.Dequeue(), deck.Dequeue() };
        var game = new BlackjackGame(userId, bet, playerCards, dealerCards, deck);

        if (!_games.TryAdd(userId, game))
        {
            await _coinService.AddCoinsAsync(userId, bet);
            throw new InvalidOperationException("⚠️ 現在ブラックジャックをプレイ中です。");
        }

        if (CalculateTotal(game.PlayerCards) == 21)
            return await FinishAsync(game, BlackjackOutcome.Blackjack);

        return CreateResult(game, false, "あなたのターンです。", 0);
    }

    public async Task<BlackjackResult> HitAsync(ulong userId)
    {
        if (!_games.TryGetValue(userId, out var game))
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

    public async Task<BlackjackResult> StandAsync(ulong userId)
    {
        if (!_games.TryGetValue(userId, out var game))
            throw new InvalidOperationException("このブラックジャックゲームは終了しています。");

        lock (game)
        {
            if (game.IsFinished)
                throw new InvalidOperationException("このブラックジャックゲームは終了しています。");
            while (CalculateTotal(game.DealerCards) < 17)
                game.DealerCards.Add(game.Deck.Dequeue());
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

    public bool IsOwner(ulong userId, ulong gameOwnerId) => userId == gameOwnerId && _games.ContainsKey(gameOwnerId);

    public static int CalculateTotal(IReadOnlyList<BlackjackCard> cards)
    {
        var total = cards.Sum(card => card.BaseValue);
        var aces = cards.Count(card => card.Rank == "A");
        while (total > 21 && aces-- > 0)
            total -= 10;
        return total;
    }

    private async Task<BlackjackResult> FinishAsync(BlackjackGame game, BlackjackOutcome outcome)
    {
        lock (game)
        {
            if (game.IsFinished)
                throw new InvalidOperationException("このブラックジャックゲームは終了しています。");
            game.IsFinished = true;
        }

        _games.TryRemove(game.UserId, out _);
        var payout = outcome switch
        {
            BlackjackOutcome.Blackjack => game.Bet * 3 / 2,
            BlackjackOutcome.Win => game.Bet * 2,
            BlackjackOutcome.Push => game.Bet,
            _ => 0
        };
        if (payout > 0)
            await _coinService.AddCoinsAsync(game.UserId, payout);

        var message = outcome switch
        {
            BlackjackOutcome.Blackjack => "🎉 BLACKJACK! 1.5倍払い戻し",
            BlackjackOutcome.Win => "🎉 勝利！",
            BlackjackOutcome.Push => "🤝 PUSH（引き分け）",
            _ => CalculateTotal(game.PlayerCards) > 21 ? "💥 BUST!" : "😢 負け"
        };
        return CreateResult(game, true, message, payout);
    }

    private static BlackjackResult CreateResult(BlackjackGame game, bool finished, string message, long payout)
    {
        var dealer = finished
            ? string.Join(" ", game.DealerCards)
            : $"🂠 {game.DealerCards[1]}";
        var embed = new DiscordEmbedBuilder()
            .WithTitle("🃏 BLACKJACK")
            .WithDescription($"**あなた**\n{string.Join(" ", game.PlayerCards)}\n合計: {CalculateTotal(game.PlayerCards)}\n\n**ディーラー**\n{dealer}\n合計: {(finished ? CalculateTotal(game.DealerCards).ToString() : "❓")}\n\n{message}\nベット: {game.Bet:N0}\n払い戻し: {payout:N0}")
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

    private enum BlackjackOutcome { Loss, Push, Win, Blackjack }
}

public sealed record BlackjackResult(DiscordEmbed Embed, bool IsFinished);
