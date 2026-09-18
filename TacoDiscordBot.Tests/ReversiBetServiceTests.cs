using TacoDiscordBot.Models;
using Xunit;

namespace TacoDiscordBot.Tests;

public sealed class ReversiBetServiceTests
{
    [Theory]
    [InlineData(33, 31, 1.0)]
    [InlineData(36, 28, 1.2)]
    [InlineData(40, 24, 1.8)]
    [InlineData(44, 20, 2.4)]
    [InlineData(48, 16, 3.4)]
    [InlineData(52, 12, 4.7)]
    [InlineData(56, 8, 6.2)]
    [InlineData(58, 6, 7.0)]
    [InlineData(60, 4, 8.0)]
    [InlineData(62, 2, 8.0)]
    public void 指定された石数比から倍率を計算する(int winnerStones, int loserStones, double expectedMultiplier)
    {
        var result = ReversiBetCalculator.Calculate(100, winnerStones, loserStones);

        Assert.Equal((decimal)expectedMultiplier, result.Multiplier);
    }

    [Fact]
    public void 完勝時は10倍になる()
    {
        var result = ReversiBetCalculator.Calculate(100, 64, 0);

        Assert.Equal(10m, result.Multiplier);
        Assert.Equal(1_800, result.AdditionalLoss);
        Assert.Equal(2_000, result.Payout);
    }

    [Fact]
    public void 精算結果はゼロサムになる()
    {
        var result = ReversiBetCalculator.Calculate(100, 44, 20);

        Assert.Equal(480, result.Payout);
        Assert.Equal(280, result.AdditionalLoss);
        Assert.Equal(380, result.WinnerNet);
        Assert.Equal(-380, result.LoserNet);
        Assert.Equal(0, result.WinnerNet + result.LoserNet);
    }
}
