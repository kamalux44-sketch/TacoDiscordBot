using System;
using System.Threading.Tasks;
using Moq;
using TacoDiscordBot.Commands;
using TacoDiscordBot.Contexts;
using TacoDiscordBot.Services.Interface;
using TacoDiscordBot.Util;
using Xunit;

namespace TacoDiscordBot.Tests.CommandTest;

public class BoCommandsTests
{
    [Fact]
    public async Task 正常な入力の場合は募集を作成して完了メッセージを返す()
    {
        var response = CreateResponseMock();
        var manager = new Mock<IBoManager>();

        await new BoCommands().BoAsync(
            response.Object,
            null,
            "ゲーム募集",
            3,
            "A",
            "2026-08-13 01:30",
            "説明",
            manager.Object
        );

        manager.Verify(x => x.CreateSessionAsync(
            null,
            "ゲーム募集",
            3,
            "A",
            It.Is<DateTime?>(value => value.HasValue
                && value.Value.Kind == DateTimeKind.Unspecified
                && value.Value == new DateTime(2026, 8, 13, 1, 30, 0)),
            "説明"), Times.Once);
        response.Verify(x => x.DeferResponseAsync(), Times.Once);
        response.Verify(x => x.EditResponseAsync(Strings.BoCreatedConfirmation), Times.Once);
    }

    [Fact]
    public async Task 締め切りが空の場合は締め切りなしで募集を作成する()
    {
        var response = CreateResponseMock();
        var manager = new Mock<IBoManager>();

        await new BoCommands().BoAsync(
            response.Object,
            null,
            manager: manager.Object
        );

        manager.Verify(x => x.CreateSessionAsync(
            null,
            "",
            0,
            "",
            null,
            ""), Times.Once);
    }

    [Theory]
    [InlineData("2026/08/13 01:30")]
    [InlineData("2026-13-01 01:30")]
    [InlineData("invalid")]
    public async Task 締め切り形式が不正な場合は募集を作成しない(string deadline)
    {
        var response = CreateResponseMock();
        var manager = new Mock<IBoManager>();

        await new BoCommands().BoAsync(
            response.Object,
            null,
            deadline: deadline,
            manager: manager.Object
        );

        response.Verify(x => x.EditResponseAsync(Strings.BoDeadlineInvalid), Times.Once);
        manager.Verify(x => x.CreateSessionAsync(
            It.IsAny<DSharpPlus.SlashCommands.InteractionContext>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.IsAny<DateTime?>(),
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task 募集サービスが未設定の場合は非公開エラーを返す()
    {
        var response = CreateResponseMock();

        await new BoCommands().BoAsync(response.Object, null, manager: null);

        response.Verify(x => x.RespondAsync("募集サービスは未設定です。", true), Times.Once);
        response.Verify(x => x.DeferResponseAsync(), Times.Never);
    }

    private static Mock<IInteractionResponseContext> CreateResponseMock()
    {
        var response = new Mock<IInteractionResponseContext>();
        response.Setup(x => x.RespondAsync(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);
        response.Setup(x => x.DeferResponseAsync()).Returns(Task.CompletedTask);
        response.Setup(x => x.EditResponseAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        return response;
    }
}