using System.Collections.Generic;
using TacoDiscordBot.Services;
using Xunit;

namespace TacoDiscordBot.Tests;

public sealed class RouletteServiceTests
{
    [Theory]
    [InlineData(100, 1, 1, 200)]
    [InlineData(100, 3, 3, 400)]
    [InlineData(100, 5, 5, 600)]
    [InlineData(100, 10, 10, 1100)]
    [InlineData(100, 20, 20, 2100)]
    public void 的中時は数字ごとの倍率で払い戻す(long bet, int prediction, int result, long expected)
    {
        var multipliers = RouletteConfiguration.Default.PayoutMultipliers;

        Assert.Equal(expected, RouletteService.CalculatePayout(bet, prediction, result, multipliers));
    }

    [Fact]
    public void ハズレ時の払い戻しはゼロになる()
    {
        var multipliers = RouletteConfiguration.Default.PayoutMultipliers;

        Assert.Equal(0, RouletteService.CalculatePayout(500, 5, 3, multipliers));
    }

    [Fact]
    public void デフォルトホイールは25マスで構成される()
    {
        var wheel = RouletteConfiguration.Default.Wheel;

        Assert.Equal(25, wheel.Count);
        Assert.Equal(12, CountOccurrences(wheel, 1));
        Assert.Equal(6, CountOccurrences(wheel, 3));
        Assert.Equal(4, CountOccurrences(wheel, 5));
        Assert.Equal(2, CountOccurrences(wheel, 10));
        Assert.Equal(1, CountOccurrences(wheel, 20));
    }

    private static int CountOccurrences(IReadOnlyList<int> values, int target)
    {
        var count = 0;
        foreach (var value in values)
        {
            if (value == target)
                count++;
        }

        return count;
    }
}
