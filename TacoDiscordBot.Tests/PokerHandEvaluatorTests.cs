using System.Linq;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services;
using Xunit;

namespace TacoDiscordBot.Tests;

public sealed class PokerHandEvaluatorTests
{
    [Fact]
    public void ロイヤルフラッシュを最強役として判定する()
    {
        var hand = Cards(PokerSuit.Spades, PokerRank.Ten, PokerRank.Jack, PokerRank.Queen, PokerRank.King, PokerRank.Ace);

        var result = PokerHandEvaluator.Evaluate(hand);

        Assert.Equal(PokerHandCategory.RoyalFlush, result.Category);
    }

    [Fact]
    public void A2345をストレートとして判定する()
    {
        var hand = new[]
        {
            new PokerCard(PokerRank.Ace, PokerSuit.Spades),
            new PokerCard(PokerRank.Two, PokerSuit.Hearts),
            new PokerCard(PokerRank.Three, PokerSuit.Diamonds),
            new PokerCard(PokerRank.Four, PokerSuit.Clubs),
            new PokerCard(PokerRank.Five, PokerSuit.Spades)
        };

        var result = PokerHandEvaluator.Evaluate(hand);

        Assert.Equal(PokerHandCategory.Straight, result.Category);
        Assert.Equal([5], result.TieBreakValues);
    }

    [Fact]
    public void 同じ役はキッカーで比較する()
    {
        var pairOfAces = new[]
        {
            new PokerCard(PokerRank.Ace, PokerSuit.Spades),
            new PokerCard(PokerRank.Ace, PokerSuit.Hearts),
            new PokerCard(PokerRank.King, PokerSuit.Diamonds),
            new PokerCard(PokerRank.Queen, PokerSuit.Clubs),
            new PokerCard(PokerRank.Jack, PokerSuit.Spades)
        };
        var pairOfKings = new[]
        {
            new PokerCard(PokerRank.King, PokerSuit.Spades),
            new PokerCard(PokerRank.King, PokerSuit.Hearts),
            new PokerCard(PokerRank.Queen, PokerSuit.Diamonds),
            new PokerCard(PokerRank.Jack, PokerSuit.Clubs),
            new PokerCard(PokerRank.Ten, PokerSuit.Spades)
        };

        Assert.True(PokerHandEvaluator.Evaluate(pairOfAces).CompareTo(PokerHandEvaluator.Evaluate(pairOfKings)) > 0);
    }

    [Fact]
    public void Chipを卓レートでCoinへ換算し端数を切り捨てる()
    {
        Assert.Equal(750, PokerService.CalculateCoinPayout(1500, 500));
        Assert.Equal(99, PokerService.CalculateCoinPayout(333, 300));
    }

    private static PokerCard[] Cards(PokerSuit suit, params PokerRank[] ranks)
        => ranks.Select(rank => new PokerCard(rank, suit)).ToArray();
}
