using System;
using System.Threading.Tasks;
using Moq;
using TacoDiscordBot.Commands;
using TacoDiscordBot.Contexts;
using TacoDiscordBot.Services;
using TacoDiscordBot.Util;
using Xunit;

namespace TacoDiscordBot.Tests.CommandTest;

public class VcLogCommandsTests
{
    [Fact]
    public async Task ギルド外では設定処理を行わず非公開メッセージを返す()
    {
        var response = CreateResponseMock();
        var logger = new Mock<IVcLogger>();

        await new VcLogCommands().VcLogAsync(response.Object, 0, 123, logger.Object);

        response.Verify(x => x.RespondAsync(Strings.CommandGuildOnly, true), Times.Once);
        logger.Verify(x => x.IsConfiguredForGuild(It.IsAny<ulong>()), Times.Never);
        logger.Verify(x => x.SetChannelAsync(It.IsAny<ulong>(), It.IsAny<ulong>()), Times.Never);
    }

    [Fact]
    public async Task 未設定のギルドでは現在のチャンネルを設定して有効化メッセージを返す()
    {
        var response = CreateResponseMock();
        var logger = new Mock<IVcLogger>();
        logger.Setup(x => x.IsConfiguredForGuild(10)).Returns(false);

        await new VcLogCommands().VcLogAsync(response.Object, 10, 123, logger.Object);

        logger.Verify(x => x.SetChannelAsync(10, 123), Times.Once);
        response.Verify(x => x.RespondAsync(Strings.VcToggleOn, true), Times.Once);
        logger.Verify(x => x.RemoveChannelAsync(It.IsAny<ulong>()), Times.Never);
    }

    [Fact]
    public async Task 設定済みのギルドでは設定を削除して無効化メッセージを返す()
    {
        var response = CreateResponseMock();
        var logger = new Mock<IVcLogger>();
        logger.Setup(x => x.IsConfiguredForGuild(10)).Returns(true);

        await new VcLogCommands().VcLogAsync(response.Object, 10, 123, logger.Object);

        logger.Verify(x => x.RemoveChannelAsync(10), Times.Once);
        response.Verify(x => x.RespondAsync(Strings.VcToggleOff, true), Times.Once);
        logger.Verify(x => x.SetChannelAsync(It.IsAny<ulong>(), It.IsAny<ulong>()), Times.Never);
    }

    [Fact]
    public async Task VCログサービス未設定の場合は非公開エラーを返す()
    {
        var response = CreateResponseMock();

        await new VcLogCommands().VcLogAsync(response.Object, 10, 123, null);

        response.Verify(x => x.RespondAsync("VCログサービスは未設定です。", true), Times.Once);
        response.Verify(x => x.RespondAsync(Strings.VcToggleOn, true), Times.Never);
    }

    [Fact]
    public async Task チャンネル設定に失敗した場合は例外を呼び出し元へ返す()
    {
        var response = CreateResponseMock();
        var logger = new Mock<IVcLogger>();
        logger.Setup(x => x.IsConfiguredForGuild(10)).Returns(false);
        logger.Setup(x => x.SetChannelAsync(10, 123))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new VcLogCommands().VcLogAsync(response.Object, 10, 123, logger.Object));

        response.Verify(x => x.RespondAsync(Strings.VcToggleOn, true), Times.Never);
    }

    private static Mock<IInteractionResponseContext> CreateResponseMock()
    {
        var response = new Mock<IInteractionResponseContext>();
        response.Setup(x => x.RespondAsync(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);
        return response;
    }
}
