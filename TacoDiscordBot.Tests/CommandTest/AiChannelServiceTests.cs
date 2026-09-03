using System.Threading.Tasks;
using TacoDiscordBot.Services;
using Xunit;

namespace TacoDiscordBot.Tests.CommandTest;

public class AiChannelServiceTests
{
    // 初期状態で対象チャンネルが未設定であることを検証します。
    // 設定後に対象判定が有効になることを検証します。
    // 同一ギルドの設定更新を検証します。
    // 設定解除後に対象判定が無効になることを検証します。
    [Fact]
    public void 初期状態ではAIチャンネルが未設定である()
    {
        var service = new AiChannelService();

        Assert.False(service.IsConfigured);
        Assert.False(service.IsTargetChannel(10, 20));
    }

    [Fact]
    public async Task チャンネルを設定すると対象チャンネルとして判定される()
    {
        var service = new AiChannelService();

        await service.SetChannelAsync(10, 20);

        Assert.True(service.IsConfigured);
        Assert.True(service.IsTargetChannel(10, 20));
        Assert.False(service.IsTargetChannel(10, 21));
        Assert.False(service.IsTargetChannel(11, 20));
    }

    [Fact]
    public async Task 同じギルドの設定を更新すると新しいチャンネルだけが対象になる()
    {
        var service = new AiChannelService();

        await service.SetChannelAsync(10, 20);
        await service.SetChannelAsync(10, 21);

        Assert.False(service.IsTargetChannel(10, 20));
        Assert.True(service.IsTargetChannel(10, 21));
    }

    [Fact]
    public async Task チャンネルを削除すると未設定になる()
    {
        var service = new AiChannelService();
        await service.SetChannelAsync(10, 20);

        await service.RemoveChannelAsync(10);

        Assert.False(service.IsConfigured);
        Assert.False(service.IsTargetChannel(10, 20));
    }

    [Fact]
    public async Task 未設定のギルドを削除しても例外にならない()
    {
        var service = new AiChannelService();

        await service.RemoveChannelAsync(999);

        Assert.False(service.IsConfigured);
    }
}
