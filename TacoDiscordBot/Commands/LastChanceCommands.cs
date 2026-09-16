using System;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services;

namespace TacoDiscordBot.Commands;

public sealed class LastChanceCommands : ApplicationCommandModule
{
    [SlashCommand("lastchance", "破産したユーザー向けの最後のチャンスです")]
    public async Task LastChance(InteractionContext ctx)
    {
        if (ctx.Guild == null)
        {
            await RespondErrorAsync(ctx, "このコマンドはサーバー内で実行してください。");
            return;
        }

        var service = BotHost.LastChanceService;
        if (service == null)
        {
            await RespondErrorAsync(ctx, "ラストチャンスサービスは未設定です。");
            return;
        }

        try
        {
            await service.StartAsync(ctx.Guild.Id, ctx.User.Id);
            await ctx.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                CreateBuilder(ctx.Guild.Id, ctx.User.Id, false)
            );
        }
        catch (InvalidOperationException ex)
        {
            await RespondErrorAsync(ctx, ex.Message);
        }
    }

    public static async Task HandleComponentInteractionAsync(
        DiscordClient client,
        ComponentInteractionCreateEventArgs e
    )
    {
        var customId = e.Interaction.Data.CustomId;
        if (string.IsNullOrWhiteSpace(customId) || !customId.StartsWith("lastchance:", StringComparison.Ordinal))
            return;

        var parts = customId.Split(':');
        if (parts.Length != 4
            || !ulong.TryParse(parts[2], out var guildId)
            || !ulong.TryParse(parts[3], out var ownerId))
            return;

        if (e.Interaction.User.Id != ownerId)
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent("❌ このラストチャンスはあなたのものではありません。")
                    .AsEphemeral(true)
            );
            return;
        }

        if (!TryParseChoice(parts[1], out var choice))
            return;

        var service = BotHost.LastChanceService;
        if (service == null)
        {
            await RespondComponentErrorAsync(e, "ラストチャンスサービスは未設定です。");
            return;
        }

        try
        {
            var result = await service.SelectAsync(guildId, ownerId, choice);
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.UpdateMessage,
                CreateResultBuilder(result, choice, guildId, ownerId)
            );
        }
        catch (InvalidOperationException ex)
        {
            await RespondComponentErrorAsync(e, ex.Message);
        }
    }

    private static DiscordInteractionResponseBuilder CreateBuilder(
        ulong guildId,
        ulong userId,
        bool disabled
    )
    {
        return new DiscordInteractionResponseBuilder()
            .WithContent(
                "🆘 **LAST CHANCE**\n\n"
                + "あなたは破産しました。\n\n"
                + "最後のチャンスです。\n"
                + "3つの宝箱から1つ選んでください。\n\n"
                + "💰 **安定**\n1,000 🪙を確実に獲得\n\n"
                + "🎰 **ギャンブル**\n0〜3,000 🪙\n\n"
                + "💎 **一発逆転**\n0 🪙 または 5,000 🪙"
            )
            .AddComponents(CreateButtons(guildId, userId, disabled));
    }

    private static DiscordInteractionResponseBuilder CreateResultBuilder(
        LastChanceResult result,
        LastChanceChoice choice,
        ulong guildId,
        ulong userId
    )
    {
        var choiceName = choice switch
        {
            LastChanceChoice.Stable => "💰 安定",
            LastChanceChoice.Gamble => "🎰 ギャンブル",
            _ => "💎 一発逆転"
        };
        var content = result.IsJackpot
            ? $"💎 **JACKPOT!!**\n\n破産からの奇跡の復活！\n\n+{result.Reward:N0} 🪙\n\n現在の所持コイン：{result.Balance:N0} 🪙"
            : $"{choiceName}を選択しました。\n\n+{result.Reward:N0} 🪙\n\n現在の所持コイン：{result.Balance:N0} 🪙";

        return new DiscordInteractionResponseBuilder()
            .WithContent(content)
            .AddComponents(CreateButtons(guildId, userId, true));
    }

    private static DiscordComponent[] CreateButtons(ulong guildId, ulong userId, bool disabled)
        => new DiscordComponent[]
        {
            new DiscordButtonComponent(ButtonStyle.Success, $"lastchance:stable:{guildId}:{userId}", "💰", disabled),
            new DiscordButtonComponent(ButtonStyle.Primary, $"lastchance:gamble:{guildId}:{userId}", "🎰", disabled),
            new DiscordButtonComponent(ButtonStyle.Danger, $"lastchance:jackpot:{guildId}:{userId}", "💎", disabled)
        };

    private static bool TryParseChoice(string value, out LastChanceChoice choice)
    {
        choice = value switch
        {
            "stable" => LastChanceChoice.Stable,
            "gamble" => LastChanceChoice.Gamble,
            "jackpot" => LastChanceChoice.Jackpot,
            _ => (LastChanceChoice)(-1)
        };
        return choice != (LastChanceChoice)(-1);
    }

    private static Task RespondErrorAsync(InteractionContext ctx, string message)
        => ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(message).AsEphemeral(true)
        );

    private static Task RespondComponentErrorAsync(ComponentInteractionCreateEventArgs e, string message)
        => e.Interaction.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(message).AsEphemeral(true)
        );
}
