using System;
using System.Globalization;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services;

namespace TacoDiscordBot.Commands;

public sealed class MinesCommands : ApplicationCommandModule
{
    [SlashCommand("mines", "爆弾を避けてコインを増やすMinesをプレイします")]
    public async Task Mines(
        InteractionContext ctx,
        [Option("bet", "1以上、所持コイン以内のベット額")] long bet
    )
    {
        if (ctx.Guild == null)
        {
            await RespondErrorAsync(ctx, "このコマンドはサーバー内で実行してください。");
            return;
        }

        var service = BotHost.MinesService;
        if (service == null)
        {
            await RespondErrorAsync(ctx, "MINESサービスは未設定です。");
            return;
        }

        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
        try
        {
            var result = await service.StartAsync(ctx.Guild.Id, ctx.User.Id, bet);
            await ctx.EditResponseAsync(CreateWebhookBuilder(result, ctx.Guild.Id, ctx.User.Id));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(ex.Message));
        }
    }

    public static async Task HandleComponentInteractionAsync(
        DiscordClient client,
        ComponentInteractionCreateEventArgs e
    )
    {
        var customId = e.Interaction.Data.CustomId;
        if (string.IsNullOrWhiteSpace(customId) || !customId.StartsWith("mines:", StringComparison.Ordinal))
            return;

        var parts = customId.Split(':');
        if (parts.Length < 4
            || !ulong.TryParse(parts[2], out var guildId)
            || !ulong.TryParse(parts[3], out var ownerId))
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

        var service = BotHost.MinesService;
        if (service == null)
            return;

        await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
        try
        {
            MinesResult result;
            if (parts[1] == "cashout")
            {
                result = await service.CashOutAsync(guildId, ownerId);
            }
            else if (parts[1] == "open"
                && parts.Length == 5
                && int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                result = await service.OpenAsync(guildId, ownerId, index);
            }
            else
            {
                return;
            }

            await e.Interaction.EditOriginalResponseAsync(
                CreateWebhookBuilder(result, guildId, ownerId)
            );
        }
        catch (InvalidOperationException ex)
        {
            await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithContent(ex.Message));
        }
    }

    private static DiscordInteractionResponseBuilder CreateBuilder(
        MinesResult result,
        ulong guildId,
        ulong userId
    )
    {
        var game = result.Game;
        var payout = game.State == MinesGameState.Lost ? 0 : game.CurrentAmount;
        var content = $"{result.Message}\n\n"
            + $"**🎮 Mines**\n"
            + $"賭け金: **{game.Bet:N0} Coin**\n"
            + $"開放数: **{game.SafeOpenedCount} / 16**\n"
            + $"現在倍率: **{game.Multiplier:0.##}x**\n"
            + $"現在回収額: **{payout:N0} Coin**\n\n"
            + (game.State == MinesGameState.Lost ? "最終盤面:" : "盤面:");

        var builder = new DiscordInteractionResponseBuilder().WithContent(content);
        for (var row = 0; row < MinesGame.RowCount; row++)
        {
            var buttons = new DiscordComponent[MinesGame.ColumnCount];
            for (var column = 0; column < MinesGame.ColumnCount; column++)
            {
                var index = row * MinesGame.ColumnCount + column;
                var isBomb = game.Bombs.Contains(index);
                var isVisible = game.State != MinesGameState.Playing || game.Opened.Contains(index);
                var label = isVisible ? (isBomb ? "💣" : "✅") : "□";
                var style = isVisible && isBomb
                    ? ButtonStyle.Danger
                    : isVisible ? ButtonStyle.Success : ButtonStyle.Secondary;
                buttons[column] = new DiscordButtonComponent(
                    style,
                    $"mines:open:{guildId}:{userId}:{index}",
                    label,
                    game.State != MinesGameState.Playing || isVisible
                );
            }

            builder.AddComponents(buttons);
        }

        builder.AddComponents(new DiscordButtonComponent(
            ButtonStyle.Success,
            $"mines:cashout:{guildId}:{userId}",
            "💰 CHECKOUT",
            game.State != MinesGameState.Playing
        ));
        return builder;
    }

    private static DiscordWebhookBuilder CreateWebhookBuilder(
        MinesResult result,
        ulong guildId,
        ulong userId
    )
    {
        var response = CreateBuilder(result, guildId, userId);
        var builder = new DiscordWebhookBuilder().WithContent(response.Content);
        foreach (var row in response.Components)
            builder.AddComponents(row);
        return builder;
    }

    private static Task RespondErrorAsync(InteractionContext ctx, string message)
        => ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(message).AsEphemeral(true)
        );
}
