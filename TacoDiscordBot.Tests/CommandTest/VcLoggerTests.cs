using System.Threading.Tasks;
using TacoDiscordBot.Services;
using Xunit;

namespace TacoDiscordBot.Tests.CommandTest;

public class VcLoggerTests
{
    // レガシー設定の初期状態を検証します。
    // ログ出力先の設定を検証します。
    // 設定状態の切替を検証します。
    // 非同期の設定・削除処理を検証します。

    [Fact]
    public void 初期状態ではレガシーVCログが未設定である()
    {
        var logger = new VcLogService(null, null);

        Assert.False(logger.IsConfigured);
        Assert.False(logger.IsConfiguredForGuild(10));
    }

    [Fact]
    public void レガシーチャンネルを設定すると有効になる()
    {
        var logger = new VcLogService(null, null);

        logger.SetChannel(123);

        Assert.True(logger.IsConfigured);
        Assert.True(logger.IsConfiguredForGuild(10));
    }

    [Fact]
    public void レガシー設定はToggleChannelで無効化と有効化を切り替えられる()
    {
        var logger = new VcLogService(null, null);
        logger.SetChannel(123);

        Assert.False(logger.ToggleChannel(10));
        Assert.False(logger.IsConfigured);
        Assert.True(logger.ToggleChannel(10));
        Assert.True(logger.IsConfigured);
    }

    [Fact]
    public async Task レガシー設定を非同期で更新および削除できる()
    {
        var logger = new VcLogService(null, null);

        await logger.SetChannelAsync(10, 123);
        Assert.True(logger.IsConfiguredForGuild(10));

        await logger.RemoveChannelAsync(10);
        Assert.False(logger.IsConfigured);
    }
}
