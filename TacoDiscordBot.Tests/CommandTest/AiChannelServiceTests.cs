using System.Threading.Tasks;
using TacoDiscordBot.Services;
using Xunit;

namespace TacoDiscordBot.Tests.CommandTest;

public class AiChannelServiceTests
{
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
}
