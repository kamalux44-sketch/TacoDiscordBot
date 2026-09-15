using System;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Services;

namespace TacoDiscordBot.Commands;

public sealed class BlackjackCommands : ApplicationCommandModule
{
    [SlashCommand("blackjack", "ブラックジャックをプレイします")]
    public async Task Blackjack(
        InteractionContext ctx,
        [Option("bet", "1以上、所持コイン以内のベット額")] long bet
    )
    {
        var service = BotHost.BlackjackService;
        if (service == null)
        {
            await ctx.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("ブラックジャックサービスは未設定です。").AsEphemeral(true)
            );
            return;
        }

        try
        {
            var result = await service.StartAsync(ctx.User.Id, bet);
            await ctx.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                CreateBuilder(result, ctx.User.Id)
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
        DSharpPlus.DiscordClient client,
        ComponentInteractionCreateEventArgs e
    )
    {
        var customId = e.Interaction.Data.CustomId;
        if (string.IsNullOrWhiteSpace(customId) || !customId.StartsWith("blackjack:", StringComparison.Ordinal))
            return;

        var parts = customId.Split(':');
        if (parts.Length != 3 || !ulong.TryParse(parts[2], out var ownerId))
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

        var service = BotHost.BlackjackService;
        if (service == null)
            return;

        try
        {
            var result = parts[1] switch
            {
                "hit" => await service.HitAsync(ownerId),
                "stand" => await service.StandAsync(ownerId),
                _ => null
            };
            if (result == null)
                return;

            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.UpdateMessage,
                CreateBuilder(result, ownerId)
            );
        }
        catch (InvalidOperationException ex)
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent(ex.Message).AsEphemeral(true)
            );
        }
    }

    private static DiscordInteractionResponseBuilder CreateBuilder(BlackjackResult result, ulong userId)
    {
        var builder = new DiscordInteractionResponseBuilder().AddEmbed(result.Embed);
        if (!result.IsFinished)
        {
            builder.AddComponents(
                new DiscordComponent[]
                {
                    new DiscordButtonComponent(ButtonStyle.Primary, $"blackjack:hit:{userId}", "HIT"),
                    new DiscordButtonComponent(ButtonStyle.Success, $"blackjack:stand:{userId}", "STAND")
                }
            );
        }
        return builder;
    }

    private static Task RespondErrorAsync(InteractionContext ctx, string message)
        => ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(message).AsEphemeral(true)
        );
}
