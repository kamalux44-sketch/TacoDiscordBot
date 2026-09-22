using System;
using TacoDiscordBot.Services;
using TacoDiscordBot.Models;
using Xunit;

namespace TacoDiscordBot.Tests;

public sealed class BlackjackServiceTests
{
    [Fact]
    public void サレンダーの偶数ベットは半額返金になる()
    {
        Assert.Equal(50, BlackjackService.CalculateSurrenderPayout(100));
    }

    [Fact]
    public void サレンダーの奇数ベットは小数コインを切り捨てる()
    {
        Assert.Equal(50, BlackjackService.CalculateSurrenderPayout(101));
    }

    [Fact]
    public void ブラックジャックの予定払い戻し額はベットの3倍になる()
    {
        Assert.Equal(300, BlackjackService.CalculateScheduledPayout(100, BlackjackOutcome.Blackjack));
    }

    [Fact]
    public void 通常勝利の予定払い戻し額はベットの2倍になる()
    {
        Assert.Equal(200, BlackjackService.CalculateScheduledPayout(100, BlackjackOutcome.Win));
    }

    [Fact]
    public void 敗北時は通常勝利相当額を予定払い戻し額とする()
    {
        Assert.Equal(200, BlackjackService.CalculateScheduledPayout(100, BlackjackOutcome.Loss));
    }

    [Fact]
    public void サレンダーは半敗として勝率を計算する()
    {
        var statistics = new BlackjackStatistics
        {
            Wins = 100,
            LossHalfUnits = 181,
            Draws = 10
        };

        Assert.Equal(50, Math.Round(statistics.WinRate));
    }

    [Fact]
    public void ダブルダウンは初期ベットと同額だけ現在ベットを増やす()
    {
        var game = new BlackjackGame(
            1,
            2,
            1_000,
            new[] { new BlackjackCard("8", "♠"), new BlackjackCard("3", "♥") },
            new[] { new BlackjackCard("K", "♣"), new BlackjackCard("7", "♦") },
            Array.Empty<BlackjackCard>());

        game.DoubleBet();

        Assert.Equal(1_000, game.OriginalBet);
        Assert.Equal(2_000, game.CurrentBet);
        Assert.True(game.IsDoubledDown);
    }
}
