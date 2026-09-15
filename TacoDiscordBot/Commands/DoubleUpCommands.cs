using System;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services;

namespace TacoDiscordBot.Commands;

public sealed class DoubleUpCommands : ApplicationCommandModule
{
    [SlashCommand("doubleup", "コインを2倍にするゲームをプレイします")]
    public async Task DoubleUp(
        InteractionContext ctx,
        [Option("bet", "1以上、所持コイン以内のベット額")] long bet
    )
    {
        if (ctx.Guild == null)
        {
            await RespondErrorAsync(ctx, "このコマンドはサーバー内で実行してください。");
            return;
        }

        var service = BotHost.DoubleUpService;
        if (service == null)
        {
            await RespondErrorAsync(ctx, "DOUBLE UP サービスは未設定です。");
            return;
        }

        try
        {
            var result = await service.StartAsync(ctx.Guild.Id, ctx.User.Id, bet);
            await ctx.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                CreateBuilder(result, ctx.Guild.Id, ctx.User.Id)
            );
        }
        catch (ArgumentOutOfRangeException ex)
        {
            await RespondErrorAsync(ctx, ex.Message);
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
        if (string.IsNullOrWhiteSpace(customId) || !customId.StartsWith("doubleup:", StringComparison.Ordinal))
            return;

        var parts = customId.Split(':');
        if (parts.Length != 4 || !ulong.TryParse(parts[2], out var guildId) || !ulong.TryParse(parts[3], out var ownerId))
            return;

        if (e.Interaction.User.Id != ownerId)
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent("⚠️ このゲームを操作できるのはゲーム開始者だけです。")
                    .AsEphemeral(true)
            );
            return;
        }

        var service = BotHost.DoubleUpService;
        if (service == null)
            return;

        try
        {
            DoubleUpResult result;
            if (parts[1] == "cashout")
            {
                result = await service.CashOutAsync(guildId, ownerId);
                await e.Interaction.CreateResponseAsync(
                    InteractionResponseType.UpdateMessage,
                    CreateBuilder(result, guildId, ownerId)
                );
                return;
            }

            if (parts[1] != "high" && parts[1] != "low")
                return;

            var choice = parts[1] == "high" ? DoubleUpChoice.High : DoubleUpChoice.Low;
            result = await service.SelectAsync(
                guildId,
                ownerId,
                choice,
                () => e.Interaction.CreateResponseAsync(
                    InteractionResponseType.UpdateMessage,
                    CreateBuilder(null, guildId, ownerId, true)
                )
            );
            await e.Interaction.EditOriginalResponseAsync(CreateWebhookBuilder(result, guildId, ownerId));
        }
        catch (InvalidOperationException ex)
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent(ex.Message).AsEphemeral(true)
            );
        }
    }

    private static DiscordInteractionResponseBuilder CreateBuilder(
        DoubleUpResult? result,
        ulong guildId,
        ulong userId,
        bool isDrawing = false
    )
    {
        var builder = new DiscordInteractionResponseBuilder();
        if (result != null)
            builder.AddEmbed(result.Embed);

        if (isDrawing)
        {
            builder.AddEmbed(new DiscordEmbedBuilder()
                .WithTitle("🎰 DOUBLE UP")
                .WithDescription("━━━━━━━━━━━━━━\n\n🔮 カードをめくります...\n\n🂠\n\n🤔 さて、このカードは7より...?\n\n━━━━━━━━━━━━━━")
                .WithColor(DiscordColor.Blurple)
                .Build());
            builder.AddComponents(CreateChoiceButtons(guildId, userId, true));
            return builder;
        }

        if (result?.CanSelect == true)
        {
            if (result.IsMaxMultiplier)
            {
                builder.AddComponents(new DiscordButtonComponent(
                    ButtonStyle.Success,
                    $"doubleup:cashout:{guildId}:{userId}",
                    "💰 CASH OUT"
                ));
            }
            else
            {
                builder.AddComponents(CreateChoiceButtons(guildId, userId, false));
                builder.AddComponents(
                    new DiscordButtonComponent(
                        ButtonStyle.Success,
                        $"doubleup:cashout:{guildId}:{userId}",
                        "💰 CASH OUT"
                    )
                );
            }
        }

        return builder;
    }

    private static DiscordWebhookBuilder CreateWebhookBuilder(
        DoubleUpResult result,
        ulong guildId,
        ulong userId
    )
    {
        var builder = new DiscordWebhookBuilder().AddEmbed(result.Embed);
        if (!result.CanSelect)
            return builder;

        if (result.IsMaxMultiplier)
        {
            builder.AddComponents(new DiscordButtonComponent(
                ButtonStyle.Success,
                $"doubleup:cashout:{guildId}:{userId}",
                "💰 CASH OUT"
            ));
        }
        else
        {
            builder.AddComponents(CreateChoiceButtons(guildId, userId, false));
            builder.AddComponents(new DiscordButtonComponent(
                ButtonStyle.Success,
                $"doubleup:cashout:{guildId}:{userId}",
                "💰 CASH OUT"
            ));
        }

        return builder;
    }

    private static DiscordComponent[] CreateChoiceButtons(ulong guildId, ulong userId, bool disabled)
        =>
        [
            new DiscordButtonComponent(ButtonStyle.Primary, $"doubleup:high:{guildId}:{userId}", "🔺 HIGH", disabled),
            new DiscordButtonComponent(ButtonStyle.Primary, $"doubleup:low:{guildId}:{userId}", "🔻 LOW", disabled)
        ];

    private static Task RespondErrorAsync(InteractionContext ctx, string message)
        => ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(message).AsEphemeral(true)
        );
}
