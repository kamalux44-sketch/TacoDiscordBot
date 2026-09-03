using System;
using System.Threading.Tasks;
using Moq;
using TacoDiscordBot.Services;
using TacoDiscordBot.Services.Interface;
using Xunit;

namespace TacoDiscordBot.Tests.CommandTest;

public class DeadlineServiceTests
{
    // 締切所有者が未設定の場合に安全に終了することを検証します。
    // インタラクションが null の場合に安全に終了することを検証します。
    [Fact]
    public void 所有者がnullの場合は例外を発生させる()
    {
        Assert.Throws<ArgumentNullException>(() => new DeadlineService(null));
    }

    [Fact]
    public async Task nullのインタラクションは対象外として扱う()
    {
        var owner = new Mock<IDeadlineOwner>();
        var service = new DeadlineService(owner.Object);

        var result = await service.HandleInteractionAsync(null);

        Assert.False(result);
        owner.Verify(x => x.ApplyDeadlineToLatestSessionAsync(
            It.IsAny<ulong>(), It.IsAny<DateTime>(), It.IsAny<string>()), Times.Never);
    }
}