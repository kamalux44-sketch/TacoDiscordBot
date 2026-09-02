using System;
using System.Threading.Tasks;
using Moq;
using TacoDiscordBot.Commands;
using TacoDiscordBot.Contexts;
using TacoDiscordBot.Services.Interface;
using TacoDiscordBot.Util;
using Xunit;

namespace TacoDiscordBot.Tests.CommandTest;

public class AIChannelCommandsTests
{
    [Fact]
    public async Task ギルド外では設定処理を行わず非公開メッセージを返す()
    {
        var response = CreateResponseMock();
        var service = new Mock<IAiChannelService>();

        await new AIChannelCommands().AiChannelAsync(
            response.Object,
            0,
            123,
            "general",
            service.Object
        );

        response.Verify(x => x.RespondAsync(Strings.CommandGuildOnly, true), Times.Once);
        service.Verify(x => x.SetChannelAsync(It.IsAny<ulong>(), It.IsAny<ulong>()), Times.Never);
    }

    [Fact]
    public async Task サービス未設定の場合は非公開エラーを返す()
    {
        var response = CreateResponseMock();

        await new AIChannelCommands().AiChannelAsync(
            response.Object,
            10,
            123,
            "general",
            null
        );

        response.Verify(x => x.RespondAsync(
            "AI サービスは未設定です。管理者が DB と GEMINI_API_KEY を設定しているか確認してください。",
            false), Times.Once);
    }

    [Fact]
    public async Task 正常な場合は現在のチャンネルを設定して完了メッセージを返す()
    {
        var response = CreateResponseMock();
        var service = new Mock<IAiChannelService>();

        await new AIChannelCommands().AiChannelAsync(
            response.Object,
            10,
            123,
            "general",
            service.Object
        );

        service.Verify(x => x.SetChannelAsync(10, 123), Times.Once);
        response.Verify(x => x.RespondAsync(
            "このチャンネルを AI 会話チャンネルとして設定しました。 (#general)",
            true), Times.Once);
    }

    [Fact]
    public async Task チャンネル設定に失敗した場合は例外を呼び出し元へ返す()
    {
        var response = CreateResponseMock();
        var service = new Mock<IAiChannelService>();
        service.Setup(x => x.SetChannelAsync(10, 123))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AIChannelCommands().AiChannelAsync(
                response.Object,
                10,
                123,
                "general",
                service.Object
            ));

        response.Verify(x => x.RespondAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task チャンネル名が空でも設定処理を実行する()
    {
        var response = CreateResponseMock();
        var service = new Mock<IAiChannelService>();

        await new AIChannelCommands().AiChannelAsync(
            response.Object,
            10,
            123,
            "",
            service.Object
        );

        service.Verify(x => x.SetChannelAsync(10, 123), Times.Once);
        response.Verify(x => x.RespondAsync(
            "このチャンネルを AI 会話チャンネルとして設定しました。 (#)",
            true), Times.Once);
    }

    private static Mock<IInteractionResponseContext> CreateResponseMock()
    {
        var response = new Mock<IInteractionResponseContext>();
        response.Setup(x => x.RespondAsync(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);
        return response;
    }
}
