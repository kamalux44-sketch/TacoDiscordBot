using System.Collections.Concurrent;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services;

namespace TacoDiscordBot.Commands;

public sealed class TexasPokerCommands : ApplicationCommandModule
{
    private const string Prefix = "texaspoker:";
    private static readonly ConcurrentDictionary<(string GameId, ulong UserId), EphemeralReference> EphemeralMessages = new();

    private sealed record EphemeralReference(DiscordInteraction Interaction, ulong MessageId, bool IsFollowup);

    [SlashCommand("texaspoker", "Texas Hold'emのゲームロビーを作成します")]
    public async Task CreateAsync(InteractionContext ctx)
    {
        if (ctx.Guild == null)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("このコマンドはサーバー内で実行してください。").AsEphemeral(true));
            return;
        }

        var service = BotHost.TexasPokerService;
        if (service == null)
            return;

        var game = service.CreateGame(ctx.Guild.Id, ctx.User.Id, ctx.Channel.Id);
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, BuildPublic(game));
        var message = await ctx.GetOriginalResponseAsync();
        service.SetPublicMessageId(game.GameId, message.Id);
    }

    public static async Task HandleComponentInteractionAsync(DiscordClient client, ComponentInteractionCreateEventArgs e)
    {
        var customId = e.Interaction.Data.CustomId;
        if (string.IsNullOrWhiteSpace(customId) || !customId.StartsWith(Prefix, StringComparison.Ordinal))
            return;

        var parts = customId.Split(':');
        if (parts.Length < 2 || BotHost.TexasPokerService is not { } service)
            return;

        try
        {
            switch (parts[1])
            {
                case "join":
                    await JoinAsync(e, service, parts.ElementAtOrDefault(2));
                    break;
                case "start":
                    await StartAsync(e, service, parts.ElementAtOrDefault(2));
                    break;
                case "cancel":
                    await CancelAsync(e, service, parts.ElementAtOrDefault(2));
                    break;
                case "refresh":
                    await RefreshAsync(e, service, parts.ElementAtOrDefault(2));
                    break;
                case "action" when parts.Length == 4:
                    await ActionAsync(e, service, parts[2], parts[3]);
                    break;
                case "bet" or "raise":
                    await ShowAmountModalAsync(e, parts[1], parts.ElementAtOrDefault(2));
                    break;
            }
        }
        catch (InvalidOperationException ex)
        {
            await RespondErrorAsync(e, ex.Message);
        }
    }

    public static async Task HandleModalSubmitAsync(DiscordClient client, ModalSubmitEventArgs e)
    {
        var customId = e.Interaction.Data.CustomId;
        if (string.IsNullOrWhiteSpace(customId) || !customId.StartsWith(Prefix, StringComparison.Ordinal))
            return;

        var parts = customId.Split(':');
        if (parts.Length != 3 || parts[1] is not ("bet" or "raise") || BotHost.TexasPokerService is not { } service)
            return;

        try
        {
            if (!e.Values.TryGetValue("amount", out var value) || !long.TryParse(value, out var amount))
                throw new InvalidOperationException("ベット額には整数を入力してください。");
            var action = parts[1] == "bet" ? TexasPokerAction.Bet : TexasPokerAction.Raise;
            var game = service.ApplyAction(parts[2], e.Interaction.User.Id, action, amount);
            await e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("アクションを受け付けました。公開卓を確認してください。").AsEphemeral(true));
            if (game.PublicMessageId.HasValue)
            {
                var channel = await client.GetChannelAsync(game.ChannelId);
                var message = await channel.GetMessageAsync(game.PublicMessageId.Value);
                var publicContent = CreatePublicContent(game);
                await message.ModifyAsync(publicContent);
            }
            await UpdateEphemeralMessagesAsync(service, game);
        }
        catch (InvalidOperationException ex)
        {
            await RespondModalErrorAsync(e, ex.Message);
        }
    }

    private static async Task JoinAsync(ComponentInteractionCreateEventArgs e, TexasPokerService service, string? gameId)
    {
        if (gameId == null)
            throw new InvalidOperationException("ゲームIDがありません。");
        service.Join(gameId, e.Interaction.User.Id, e.Interaction.User.Username);
        await e.Interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
            BuildPublic(service.Find(gameId)!));
        await SendPrivateFollowupAsync(e, service, gameId, "ゲーム開始待ちです。Hole Cardsはゲーム開始時に配布されます。");
    }

    private static async Task StartAsync(ComponentInteractionCreateEventArgs e, TexasPokerService service, string? gameId)
    {
        if (gameId == null)
            throw new InvalidOperationException("ゲームIDがありません。");
        var game = service.Start(gameId, e.Interaction.User.Id);
        await e.Interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage, BuildPublic(game));
        await UpdateEphemeralMessagesAsync(service, game);
    }

    private static async Task CancelAsync(ComponentInteractionCreateEventArgs e, TexasPokerService service, string? gameId)
    {
        if (gameId == null)
            throw new InvalidOperationException("ゲームIDがありません。");
        service.Cancel(gameId, e.Interaction.User.Id);
        await e.Interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage, BuildPublic(service.Find(gameId)!));
    }

    private static async Task RefreshAsync(ComponentInteractionCreateEventArgs e, TexasPokerService service, string? gameId)
    {
        if (gameId == null)
            throw new InvalidOperationException("ゲームIDがありません。");
        await SendPrivateAsync(e, service, gameId, null);
    }

    private static async Task ActionAsync(ComponentInteractionCreateEventArgs e, TexasPokerService service, string gameId, string actionText)
    {
        if (!Enum.TryParse<TexasPokerAction>(actionText, true, out var action))
            throw new InvalidOperationException("無効な操作です。");
        var game = service.ApplyAction(gameId, e.Interaction.User.Id, action);
        await e.Interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage, BuildPublic(game));
        await UpdateEphemeralMessagesAsync(service, game);
    }

    private static async Task ShowAmountModalAsync(ComponentInteractionCreateEventArgs e, string operation, string? gameId)
    {
        if (gameId == null)
            throw new InvalidOperationException("ゲームIDがありません。");
        await e.Interaction.CreateResponseAsync(InteractionResponseType.Modal,
            new DiscordInteractionResponseBuilder()
                .WithCustomId($"{Prefix}{operation}:{gameId}")
                .WithTitle(operation == "bet" ? "ベット額" : "レイズ後の合計額")
                .AddComponents(new TextInputComponent("金額", "amount", "例: 100", null, true, TextInputStyle.Short, 1, 18)));
    }

    private static async Task SendPrivateAsync(ComponentInteractionCreateEventArgs e, TexasPokerService service, string gameId, string? notice)
    {
        var player = service.GetPlayer(gameId, e.Interaction.User.Id);
        var game = service.Find(gameId)!;
        var content = CreatePrivateContent(service, game, player, notice);
        var builder = new DiscordInteractionResponseBuilder().WithContent(content).AsEphemeral(true);
        if (game.Phase is not (TexasPokerPhase.Waiting or TexasPokerPhase.Finished or TexasPokerPhase.Cancelled)
            && game.CurrentPlayerIndex >= 0 && game.Players[game.CurrentPlayerIndex].UserId == player.UserId)
        {
            builder.AddComponents(new DiscordButtonComponent(ButtonStyle.Primary, $"{Prefix}action:{gameId}:check", "チェック"));
            if (game.CurrentBet == player.CurrentBet)
                builder.AddComponents(new DiscordButtonComponent(ButtonStyle.Primary, $"{Prefix}bet:{gameId}", "ベット"));
            else
                builder.AddComponents(new DiscordButtonComponent(ButtonStyle.Primary, $"{Prefix}action:{gameId}:call", $"コール {game.CurrentBet - player.CurrentBet}"));
            builder.AddComponents(new DiscordButtonComponent(ButtonStyle.Danger, $"{Prefix}action:{gameId}:fold", "フォールド"));
        }
        await e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, builder);
        var response = await e.Interaction.GetOriginalResponseAsync();
        EphemeralMessages[(gameId, player.UserId)] = new EphemeralReference(e.Interaction, response.Id, false);
    }

    private static async Task SendPrivateFollowupAsync(ComponentInteractionCreateEventArgs e, TexasPokerService service, string gameId, string? notice)
    {
        var player = service.GetPlayer(gameId, e.Interaction.User.Id);
        var game = service.Find(gameId)!;
        var builder = new DiscordFollowupMessageBuilder()
            .WithContent(CreatePrivateContent(service, game, player, notice))
            .AsEphemeral(true);
        if (game.CurrentPlayerIndex >= 0 && game.Players[game.CurrentPlayerIndex].UserId == player.UserId)
        {
            builder.AddComponents(new DiscordButtonComponent(ButtonStyle.Primary, $"{Prefix}action:{gameId}:check", "チェック"));
            builder.AddComponents(new DiscordButtonComponent(ButtonStyle.Primary, $"{Prefix}bet:{gameId}", "ベット"));
            builder.AddComponents(new DiscordButtonComponent(ButtonStyle.Danger, $"{Prefix}action:{gameId}:fold", "フォールド"));
        }
        var message = await e.Interaction.CreateFollowupMessageAsync(builder);
        EphemeralMessages[(gameId, player.UserId)] = new EphemeralReference(e.Interaction, message.Id, true);
    }

    private static async Task UpdateEphemeralMessagesAsync(TexasPokerService service, TexasPokerGame game)
    {
        foreach (var player in game.Players)
        {
            if (!EphemeralMessages.TryGetValue((game.GameId, player.UserId), out var reference))
                continue;

            try
            {
                var builder = CreatePrivateWebhookBuilder(service, game, player);
                if (reference.IsFollowup)
                    await reference.Interaction.EditFollowupMessageAsync(reference.MessageId, builder);
                else
                    await reference.Interaction.EditOriginalResponseAsync(builder);
            }
            catch (DSharpPlus.Exceptions.NotFoundException)
            {
                EphemeralMessages.TryRemove((game.GameId, player.UserId), out _);
            }
            catch (DSharpPlus.Exceptions.UnauthorizedException)
            {
                EphemeralMessages.TryRemove((game.GameId, player.UserId), out _);
            }
        }
    }

    private static DiscordWebhookBuilder CreatePrivateWebhookBuilder(
        TexasPokerService service,
        TexasPokerGame game,
        TexasPokerPlayer player)
    {
        var builder = new DiscordWebhookBuilder()
            .WithContent(CreatePrivateContent(service, game, player, null));
        if (game.Phase is not (TexasPokerPhase.Waiting or TexasPokerPhase.Finished or TexasPokerPhase.Cancelled)
            && game.CurrentPlayerIndex >= 0
            && game.Players[game.CurrentPlayerIndex].UserId == player.UserId)
        {
            var components = new List<DiscordComponent>();
            var callAmount = game.CurrentBet - player.CurrentBet;
            if (callAmount == 0)
                components.Add(new DiscordButtonComponent(ButtonStyle.Primary, $"{Prefix}action:{game.GameId}:check", "チェック"));
            else
                components.Add(new DiscordButtonComponent(ButtonStyle.Primary, $"{Prefix}action:{game.GameId}:call", $"コール {callAmount}"));
            components.Add(new DiscordButtonComponent(
                ButtonStyle.Primary,
                callAmount == 0 ? $"{Prefix}bet:{game.GameId}" : $"{Prefix}raise:{game.GameId}",
                callAmount == 0 ? "ベット" : "レイズ"));
            components.Add(new DiscordButtonComponent(ButtonStyle.Danger, $"{Prefix}action:{game.GameId}:fold", "フォールド"));
            builder.AddComponents(components.ToArray());
        }
        return builder;
    }

    private static DiscordInteractionResponseBuilder BuildPublic(TexasPokerGame game)
    {
        var builder = new DiscordInteractionResponseBuilder().WithContent(CreatePublicContent(game));
        if (game.Phase == TexasPokerPhase.Waiting)
        {
            builder.AddComponents(new DiscordButtonComponent(ButtonStyle.Primary, $"{Prefix}join:{game.GameId}", "参加"));
            builder.AddComponents(new DiscordButtonComponent(ButtonStyle.Success, $"{Prefix}start:{game.GameId}", "開始"));
            builder.AddComponents(new DiscordButtonComponent(ButtonStyle.Danger, $"{Prefix}cancel:{game.GameId}", "キャンセル"));
        }
        else if (game.Phase is not (TexasPokerPhase.Cancelled or TexasPokerPhase.Finished))
        {
            builder.AddComponents(new DiscordButtonComponent(ButtonStyle.Primary, $"{Prefix}refresh:{game.GameId}", "手札を再表示"));
        }
        return builder;
    }

    private static string CreatePublicContent(TexasPokerGame game)
    {
        var players = game.Players.Count == 0 ? "なし" : string.Join('\n', game.Players.Select(player => $"👤 {player.DisplayName}　💰 {player.Chips:N0}"));
        if (game.Phase == TexasPokerPhase.Finished)
        {
            var showdown = string.Join('\n', game.Players.Select(player =>
                $"{(game.WinnerUserIds.Contains(player.UserId) ? "🏆" : "　")} {player.DisplayName}: {string.Join(' ', player.HoleCards)} ({game.HandRankNames.GetValueOrDefault(player.UserId, "フォールド")})"));
            players += $"\n\nShowdown\n{showdown}";
        }
        var board = game.Phase == TexasPokerPhase.Waiting ? "ゲーム開始前です。" : $"Board\n{string.Join(' ', game.CommunityCards.Select(card => card.ToString()))} {(game.CommunityCards.Count < 5 ? "🂠" : string.Empty)}\n\nPot: {game.Pot:N0}";
        var turn = game.CurrentPlayerIndex >= 0 ? $"\n\n▶ {game.Players[game.CurrentPlayerIndex].DisplayName} のターン" : string.Empty;
        var result = game.ResultText == null ? string.Empty : $"\n\n{game.ResultText}";
        return $"🃏 Texas Hold'em\n━━━━━━━━━━━━━━━━\n\n参加者:\n{players}\n\n{board}{turn}{result}";
    }

    private static string CreatePrivateContent(TexasPokerService service, TexasPokerGame game, TexasPokerPlayer player, string? notice)
    {
        var cards = player.HoleCards.Count == 0 ? "ゲーム開始時に配布されます。" : string.Join(' ', player.HoleCards.Select(card => card.ToString()));
        var board = game.CommunityCards.Count == 0 ? "なし" : string.Join(' ', game.CommunityCards.Select(card => card.ToString()));
        var state = game.CurrentPlayerIndex >= 0 && game.Players[game.CurrentPlayerIndex].UserId == player.UserId ? "▶ あなたのターン" : $"状態: {game.Phase}";
        var hand = service.GetHandEvaluation(game.GameId, player.UserId)?.CategoryDisplayName;
        var handText = hand == null ? string.Empty : $"\n\n現在の役: {hand}";
        return $"🔒 あなたの手札\n\n{cards}\n\nBoard: {board}{handText}\n\n{state}{(notice == null ? string.Empty : $"\n\n{notice}")}";
    }

    private static async Task RespondErrorAsync(ComponentInteractionCreateEventArgs e, string message)
        => await e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(message).AsEphemeral(true));

    private static async Task RespondModalErrorAsync(ModalSubmitEventArgs e, string message)
        => await e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(message).AsEphemeral(true));
}
