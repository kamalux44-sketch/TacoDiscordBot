using TacoDiscordBot.Services;
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
}
