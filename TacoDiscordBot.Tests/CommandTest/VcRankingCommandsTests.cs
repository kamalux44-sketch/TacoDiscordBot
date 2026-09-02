using System;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using Moq;
using TacoDiscordBot.Commands;
using TacoDiscordBot.Contexts;
using TacoDiscordBot.Services.Interface;
using TacoDiscordBot.Util;
using Xunit;

namespace TacoDiscordBot.Tests.CommandTest;

public class VcRankingCommandsTests
{
    [Fact]
    public async Task 正常な期間指定ではランキングを取得してEmbedを返す()
    {
        var response = CreateResponseMock();
        var service = new Mock<IVcRankingService>();
        service.Setup(x => x.BuildRankingEmbedAsync(10, "week", null, null))
            .ReturnsAsync(new DiscordEmbedBuilder().WithTitle("VCランキング"));

        await new VcRankingCommands().VcRankAsync(
            response.Object,
            10,
            null,
            null,
            "week",
            service.Object
        );

        service.Verify(x => x.BuildRankingEmbedAsync(10, "week", null, null), Times.Once);
        response.Verify(x => x.DeferResponseAsync(), Times.Once);
        response.Verify(x => x.EditResponseAsync(It.IsAny<DiscordEmbed>()), Times.Once);
    }

    [Fact]
    public async Task 期間指定が未指定でもランキングを取得する()
    {
        var response = CreateResponseMock();
        var service = new Mock<IVcRankingService>();
        service.Setup(x => x.BuildRankingEmbedAsync(10, null, null, null))
            .ReturnsAsync(new DiscordEmbedBuilder());

        await new VcRankingCommands().VcRankAsync(
            response.Object,
            10,
            null,
            null,
            null,
            service.Object
        );

        service.Verify(x => x.BuildRankingEmbedAsync(10, null, null, null), Times.Once);
    }

    [Fact]
    public async Task ギルド外ではランキングを取得せずエラーを返す()
    {
        var response = CreateResponseMock();
        var service = new Mock<IVcRankingService>();

        await new VcRankingCommands().VcRankAsync(
            response.Object,
            0,
            null,
            null,
            "all",
            service.Object
        );

        response.Verify(x => x.EditResponseAsync(Strings.CommandGuildOnly), Times.Once);
        service.Verify(x => x.BuildRankingEmbedAsync(
            It.IsAny<ulong>(),
            It.IsAny<string>(),
            It.IsAny<DiscordGuild>(),
            It.IsAny<DiscordUser>()), Times.Never);
    }

    [Fact]
    public async Task ランキングサービス未設定の場合はエラーを返す()
    {
        var response = CreateResponseMock();

        await new VcRankingCommands().VcRankAsync(
            response.Object,
            10,
            null,
            null,
            "all",
            null
        );

        response.Verify(x => x.EditResponseAsync("VCランキングサービスは未設定です。"), Times.Once);
    }

    [Fact]
    public async Task ランキング取得に失敗した場合は例外を呼び出し元へ返す()
    {
        var response = CreateResponseMock();
        var service = new Mock<IVcRankingService>();
        service.Setup(x => x.BuildRankingEmbedAsync(
                It.IsAny<ulong>(),
                It.IsAny<string>(),
                It.IsAny<DiscordGuild>(),
                It.IsAny<DiscordUser>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new VcRankingCommands().VcRankAsync(
                response.Object,
                10,
                null,
                null,
                "all",
                service.Object
            ));
    }

    private static Mock<IInteractionResponseContext> CreateResponseMock()
    {
        var response = new Mock<IInteractionResponseContext>();
        response.Setup(x => x.DeferResponseAsync()).Returns(Task.CompletedTask);
        response.Setup(x => x.EditResponseAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        response.Setup(x => x.EditResponseAsync(It.IsAny<DiscordEmbed>()))
            .Returns(Task.CompletedTask);
        return response;
    }
}
