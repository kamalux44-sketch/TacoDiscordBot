using System;
using System.Reflection;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
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

    [Fact]
    public async Task 締切操作の形式が不正な場合は対象として処理する()
    {
        var owner = new Mock<IDeadlineOwner>();
        var service = new DeadlineService(owner.Object);

        var result = await service.HandleInteractionAsync(CreateInteraction("deadline_invalid", 123));

        Assert.True(result);
        owner.Verify(x => x.ApplyDeadlineToLatestSessionAsync(
            It.IsAny<ulong>(), It.IsAny<DateTime>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task 締切操作を別ユーザーが実行した場合は拒否する()
    {
        var owner = new Mock<IDeadlineOwner>();
        var service = new DeadlineService(owner.Object);

        var result = await service.HandleInteractionAsync(CreateInteraction("deadline_date:123", 456));

        Assert.True(result);
        owner.Verify(x => x.ApplyDeadlineToLatestSessionAsync(
            It.IsAny<ulong>(), It.IsAny<DateTime>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task 未対応の締切操作は対象外として扱う()
    {
        var owner = new Mock<IDeadlineOwner>();
        var service = new DeadlineService(owner.Object);

        var result = await service.HandleInteractionAsync(CreateInteraction("deadline_unknown:123", 123));

        Assert.False(result);
    }

    private static ComponentInteractionCreateEventArgs CreateInteraction(string customId, ulong userId)
    {
        var user = (DiscordUser)Activator.CreateInstance(typeof(DiscordUser), true);
        SetBackingField(user, "<Id>k__BackingField", userId);

        var data = (DiscordInteractionData)Activator.CreateInstance(typeof(DiscordInteractionData), true);
        SetBackingField(data, "<CustomId>k__BackingField", customId);

        var interaction = (DiscordInteraction)Activator.CreateInstance(typeof(DiscordInteraction), true);
        SetBackingField(interaction, "<Data>k__BackingField", data);
        SetBackingField(interaction, "<User>k__BackingField", user);

        var args = (ComponentInteractionCreateEventArgs)Activator.CreateInstance(
            typeof(ComponentInteractionCreateEventArgs),
            true
        );
        SetBackingField(args, "<Interaction>k__BackingField", interaction);
        return args;
    }

    private static void SetBackingField(object target, string fieldName, object value)
    {
        var type = target.GetType();

        while (type != null)
        {
            var field = type.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic
            );

            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            type = type.BaseType;
        }

        throw new MissingFieldException(target.GetType().FullName, fieldName);
    }
}