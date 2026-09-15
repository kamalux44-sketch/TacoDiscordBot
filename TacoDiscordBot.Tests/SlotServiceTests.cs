using TacoDiscordBot.Services;
using Xunit;

namespace TacoDiscordBot.Tests;

public sealed class SlotServiceTests
{
    [Fact]
    public void 主要な絵柄の3つ揃いを正しいランクに判定する()
    {
        Assert.Equal(SlotWinRank.MegaJackpot, SlotService.DetermineRank(["7️⃣", "7️⃣", "7️⃣"]));
        Assert.Equal(SlotWinRank.UltraRare, SlotService.DetermineRank(["💎", "💎", "💎"]));
        Assert.Equal(SlotWinRank.BigWin, SlotService.DetermineRank(["🔔", "🔔", "🔔"]));
    }

    [Fact]
    public void 通常絵柄の3つ揃いをWINに判定する()
    {
        Assert.Equal(SlotWinRank.Win, SlotService.DetermineRank(["🍒", "🍒", "🍒"]));
    }

    [Fact]
    public void 絵柄が揃わない場合はハズレに判定する()
    {
        Assert.Equal(SlotWinRank.Loss, SlotService.DetermineRank(["🍒", "🍋", "🍇"]));
    }

    [Fact]
    public void ちょうど2つ揃いをリーチに判定する()
    {
        Assert.Equal(SlotWinRank.Reach, SlotService.DetermineRank(["🍇", "🍇", "🍋"]));
    }

    [Fact]
    public void 三つ揃いにはリーチ配当を重複させない()
    {
        var symbols = new[] { "🍒", "🍒", "🍒" };

        Assert.Equal(SlotWinRank.Win, SlotService.DetermineRank(symbols));
        Assert.Equal(1_000, SlotService.CalculatePayout(100, symbols, SlotWinRank.Win));
    }

    [Fact]
    public void リーチ配当は小数点以下を切り捨てる()
    {
        var symbols = new[] { "🍒", "🍒", "🍋" };

        Assert.Equal(50, SlotService.CalculatePayout(101, symbols, SlotWinRank.Reach));
    }

    [Fact]
    public void 対象外の絵柄はハズレに判定する()
    {
        Assert.Equal(SlotWinRank.Loss, SlotService.DetermineRank(["❌", "❌", "❌"]));
    }
}
