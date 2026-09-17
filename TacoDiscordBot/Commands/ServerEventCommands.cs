using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services;

namespace TacoDiscordBot.Commands;

public sealed class ServerEventCommands : ApplicationCommandModule
{
    private const string SelectPrefix = "serverevent:select";
    private const string ConfirmPrefix = "serverevent:confirm";
    private const string CancelPrefix = "serverevent:cancel";

    [SlashCommand("serverevent", "サーバーイベントを発令します")]
    public async Task ServerEvent(InteractionContext ctx)
    {
        if (ctx.Guild == null)
        {
            await RespondAsync(ctx, "このコマンドはサーバー内で実行してください。", true);
            return;
        }

        if (BotHost.EventManager == null)
        {
            await RespondAsync(ctx, "サーバーイベントサービスは未設定です。", true);
            return;
        }

        var options = new System.Collections.Generic.List<DiscordSelectComponentOption>();
        foreach (var definition in Services.EventManager.Definitions)
        {
            var cost = await BotHost.EventManager.CalculateCostAsync(ctx.Guild.Id, ctx.User.Id, definition.Type);
            options.Add(new DiscordSelectComponentOption(
                definition.Name,
                definition.Type.ToString(),
                $"{definition.Description} 必要コスト: {cost.Amount:N0}コイン"));
        }
        var select = new DiscordSelectComponent(
            $"{SelectPrefix}:{ctx.Guild.Id}:{ctx.User.Id}",
            "発令するイベントを選択してください",
            options,
            false,
            1,
            1);
        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent("発令するサーバーイベントを選択してください。")
                .AddComponents(select));
    }

    public static async Task HandleComponentInteractionAsync(
        DiscordClient client,
        ComponentInteractionCreateEventArgs e)
    {
        var customId = e.Interaction.Data.CustomId;
        if (string.IsNullOrWhiteSpace(customId) || !customId.StartsWith("serverevent:", StringComparison.Ordinal))
            return;

        var parts = customId.Split(':');
        if (parts.Length < 4
            || !ulong.TryParse(parts[^2], NumberStyles.None, CultureInfo.InvariantCulture, out var guildId)
            || !ulong.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var ownerId))
            return;
        if (e.Interaction.User.Id != ownerId)
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("このイベント操作は発令者だけが行えます。").AsEphemeral(true));
            return;
        }

        var manager = BotHost.EventManager;
        if (manager == null)
            return;
        if (parts[1] == "select")
        {
            await HandleSelectionAsync(e, manager, guildId, ownerId);
            return;
        }
        if (parts[1] == "confirm")
        {
            await HandleConfirmationAsync(e, manager, guildId, ownerId, parts[2]);
            return;
        }
        if (parts[1] == "cancel")
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder().WithContent("イベントの発令をキャンセルしました。"));
        }
    }

    private static async Task HandleSelectionAsync(
        ComponentInteractionCreateEventArgs e,
        EventManager manager,
        ulong guildId,
        ulong userId)
    {
        var value = e.Interaction.Data.Values?.FirstOrDefault();
        if (!Enum.TryParse<EventType>(value, out var type) || manager.GetDefinition(type) == null)
            return;

        var cost = await manager.CalculateCostAsync(guildId, userId, type);
        await e.Interaction.CreateResponseAsync(
            InteractionResponseType.UpdateMessage,
            new DiscordInteractionResponseBuilder()
                .WithContent($"イベント「{manager.GetDefinition(type).Name}」を発令します！\n\n必要コスト:\n{cost.Amount:N0}コイン（{cost.Description}）\n\nこの金額を支払いますか？")
                .AddComponents(
                    new DiscordButtonComponent(ButtonStyle.Success, $"{ConfirmPrefix}:{type}:{guildId}:{userId}", "発令する"),
                    new DiscordButtonComponent(ButtonStyle.Danger, $"{CancelPrefix}:{guildId}:{userId}", "キャンセル")));
    }

    private static async Task HandleConfirmationAsync(
        ComponentInteractionCreateEventArgs e,
        EventManager manager,
        ulong guildId,
        ulong userId,
        string eventTypeValue)
    {
        if (!Enum.TryParse<EventType>(eventTypeValue, out var type))
            return;
        try
        {
            var started = await manager.StartEventAsync(guildId, userId, type);
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder().WithContent(
                    $"{manager.GetDefinition(started.Type).Name}を発令しました。\n終了予定: {started.EndsAt.LocalDateTime:yyyy/MM/dd HH:mm:ss}"));
        }
        catch (InvalidOperationException ex)
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent(ex.Message).AsEphemeral(true));
        }
    }

    private static Task RespondAsync(InteractionContext ctx, string message, bool ephemeral)
        => ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(message).AsEphemeral(ephemeral));
}
