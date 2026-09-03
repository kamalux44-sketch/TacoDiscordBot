using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using Moq;
using TacoDiscordBot.Commands;
using TacoDiscordBot.Contexts;
using Xunit;

namespace TacoDiscordBot.Tests.CommandTest;

public class DeadlineCommandsTests
{
    // 締切選択用コンポーネントが正しく構成されることを検証します。
    // ユーザー ID が無効な場合の扱いを検証します。
    // 応答処理の例外が伝播することを検証します。

    [Fact]
    public async Task 正常な場合は日付時刻選択UIを返す()
    {
        var response = CreateResponseMock(out var components);

        await new DeadlineCommands().DeadlineAsync(response.Object, 123);

        Assert.NotNull(components);
        Assert.Equal(4, components.Count);
        Assert.Equal(3, components.OfType<DiscordSelectComponent>().Count());
        Assert.Single(components.OfType<DiscordButtonComponent>());
        Assert.Equal(25, ((DiscordSelectComponent)components[0]).Options.Count);
        Assert.Equal(24, ((DiscordSelectComponent)components[1]).Options.Count);
        Assert.Equal(12, ((DiscordSelectComponent)components[2]).Options.Count);
        Assert.Equal("deadline_date:123", components[0].CustomId);
        Assert.Equal("deadline_hour:123", components[1].CustomId);
        Assert.Equal("deadline_min:123", components[2].CustomId);
        Assert.Equal("deadline_confirm:123", components[3].CustomId);
        Assert.All(
            ((DiscordSelectComponent)components[0]).Options,
            option => Assert.Matches("^(0[1-9]|1[0-2])/[0-3][0-9]$", option.Label));
        Assert.All(
            ((DiscordSelectComponent)components[1]).Options,
            option => Assert.Matches("^[0-2][0-9]$", option.Value));
        Assert.All(
            ((DiscordSelectComponent)components[2]).Options,
            option => Assert.Matches("^[0-5][0-9]$", option.Value));
        response.Verify(x => x.RespondWithComponentsAsync(
            "**締め切り日時を選択してください**\n",
            It.IsAny<IReadOnlyList<DiscordComponent>>()), Times.Once);
    }

    [Fact]
    public async Task ユーザーIDがゼロでも選択UIを返す()
    {
        var response = CreateResponseMock(out var components);

        await new DeadlineCommands().DeadlineAsync(response.Object, 0);

        Assert.Equal("deadline_confirm:0", components[3].CustomId);
    }

    [Fact]
    public async Task Discord応答に失敗した場合は例外を呼び出し元へ返す()
    {
        var response = new Mock<IInteractionResponseContext>();
        response.Setup(x => x.RespondWithComponentsAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<DiscordComponent>>()))
            .ThrowsAsync(new InvalidOperationException("Discord error"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DeadlineCommands().DeadlineAsync(response.Object, 123));
    }

    private static Mock<IInteractionResponseContext> CreateResponseMock(
        out List<DiscordComponent> components
    )
    {
        var holder = new ComponentHolder();
        components = holder.Components;
        var response = new Mock<IInteractionResponseContext>();
        response.Setup(x => x.RespondWithComponentsAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<DiscordComponent>>()))
            .Callback<string, IReadOnlyList<DiscordComponent>>((_, value) =>
            {
                holder.Components.Clear();
                holder.Components.AddRange(value);
            })
            .Returns(Task.CompletedTask);
        return response;
    }

    private sealed class ComponentHolder
    {
        public List<DiscordComponent> Components { get; set; } = new();
    }
}
