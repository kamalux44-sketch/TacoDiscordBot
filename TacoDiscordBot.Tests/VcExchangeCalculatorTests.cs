using TacoDiscordBot.Services;
using Xunit;

namespace TacoDiscordBot.Tests;

public sealed class VcExchangeCalculatorTests
{
    [Theory]
    [InlineData(6, 6, 25)]
    [InlineData(12, 12, 50)]
    [InlineData(30, 30, 125)]
    [InlineData(60, 60, 250)]
    [InlineData(65, 60, 250)]
    [InlineData(155, 150, 625)]
    public void VC滞在時間を6分単位で換算する(long minutes, long expectedExchangeMinutes, long expectedCoins)
    {
        var exchangeMinutes = VcExchangeCalculator.GetExchangeableMinutes(minutes);

        Assert.Equal(expectedExchangeMinutes, exchangeMinutes);
        Assert.Equal(expectedCoins, VcExchangeCalculator.GetCoins(exchangeMinutes));
    }
}
