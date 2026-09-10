using System;
using TacoDiscordBot.Services;
using Xunit;

namespace TacoDiscordBot.Tests;

public sealed class BirthdayServiceTests
{
    [Fact]
    public void 実在しない誕生日を拒否する()
    {
        var result = BirthdayService.Validate(null, 2, 31);

        Assert.NotNull(result);
    }

    [Fact]
    public void うるう年の誕生日を受け付ける()
    {
        var result = BirthdayService.Validate(2000, 2, 29);

        Assert.Null(result);
    }

    [Fact]
    public void 範囲外の月日を拒否する()
    {
        Assert.NotNull(BirthdayService.Validate(null, 13, 1));
        Assert.NotNull(BirthdayService.Validate(null, 1, 32));
    }

    [Fact]
    public void 将来の誕生年を拒否する()
    {
        var result = BirthdayService.Validate(DateTime.UtcNow.Year + 1, 1, 1);

        Assert.NotNull(result);
    }
}
