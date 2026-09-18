using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services;

namespace TacoDiscordBot.Commands;

public sealed class PokerCommands : ApplicationCommandModule
{
    private const string Prefix = "poker:";

    [SlashCommand("pokerstart", "5 Card Draw Pokerの卓を作成します")]
    public async Task PokerStart(InteractionContext ctx, [Option("coin", "参加者1人あたりの参加費") ] long coin)
    {
        if (ctx.Guild == null)
        {
            await RespondErrorAsync(ctx, "このコマンドはサーバー内で実行してください。");
            return;
        }

        var service = BotHost.PokerService;
        if (service == null)
        {
            await RespondErrorAsync(ctx, "Pokerサービスは未設定です。");
            return;
        }

        try
        {
            var game = await service.CreateGameAsync(ctx.Guild.Id, ctx.User.Id, coin, ctx.Channel.Id);
            await ctx.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                CreatePublicBuilder(service.GetSnapshot(game.TableId), includeJoin: true)
            );
            var message = await ctx.GetOriginalResponseAsync();
            await service.SetPublicMessageIdAsync(game.TableId, message.Id);
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

    public static async Task ResyncRestoredGamesAsync(DiscordClient client, PokerService service)
    {
        foreach (var game in service.GetGames())
        {
            var snapshot = service.GetSnapshot(game.TableId);
            DiscordMessage? message = null;
            try
            {
                var channel = await client.GetChannelAsync(game.ChannelId);
                if (game.PublicMessageId.HasValue)
                    message = await channel.GetMessageAsync(game.PublicMessageId.Value);

                if (message == null)
                {
                    message = await channel.SendMessageAsync(
                        CreatePublicMessageBuilder(snapshot, snapshot.Phase == PokerPhase.Waiting));
                    await service.SetPublicMessageIdAsync(game.TableId, message.Id);
                }
                else
                {
                    await message.ModifyAsync(
                        CreatePublicMessageBuilder(snapshot, snapshot.Phase == PokerPhase.Waiting));
                }
            }
            catch (DSharpPlus.Exceptions.NotFoundException)
            {
                var channel = await client.GetChannelAsync(game.ChannelId);
                message = await channel.SendMessageAsync(
                    CreatePublicMessageBuilder(snapshot, snapshot.Phase == PokerPhase.Waiting));
                await service.SetPublicMessageIdAsync(game.TableId, message.Id);
            }
        }
    }

    public static async Task HandleComponentInteractionAsync(DiscordClient client, ComponentInteractionCreateEventArgs e)
    {
        var customId = e.Interaction.Data.CustomId;
        if (string.IsNullOrWhiteSpace(customId) || !customId.StartsWith(Prefix, StringComparison.Ordinal))
            return;

        var parts = customId.Split(':');
        var service = BotHost.PokerService;
        if (service == null)
            return;

        try
        {
            switch (parts.ElementAtOrDefault(1))
            {
                case "join" when parts.Length == 3:
                    await JoinAsync(client, e, service, parts[2]);
                    break;
                case "refresh" when parts.Length == 3:
                    await ShowPrivateAsync(e, service, parts[2]);
                    break;
                case "card" when parts.Length == 6 && int.TryParse(parts[4], out var cardIndex) && int.TryParse(parts[5], out var mask):
                    await ToggleCardAsync(e, service, parts[2], cardIndex, mask);
                    break;
                case "exchange" when parts.Length == 5 && int.TryParse(parts[4], out var exchangeMask):
                    await ExchangeAsync(client, e, service, parts[2], exchangeMask);
                    break;
                case "action" when parts.Length >= 4:
                    await ActionAsync(client, e, service, parts);
                    break;
            }
        }
        catch (ArgumentException ex)
        {
            await RespondErrorAsync(e, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            await RespondErrorAsync(e, ex.Message);
        }
    }

    private static async Task JoinAsync(DiscordClient client, ComponentInteractionCreateEventArgs e, PokerService service, string tableId)
    {
        if (e.Guild == null)
            throw new InvalidOperationException("サーバー内でのみ参加できます。");

        var result = await service.JoinAsync(tableId, e.Interaction.User.Id, e.Interaction.User.Username);
        await e.Interaction.CreateResponseAsync(
            InteractionResponseType.UpdateMessage,
            CreatePublicBuilder(service.GetSnapshot(tableId), includeJoin: !result.Started)
        );
        await e.Interaction.CreateFollowupMessageAsync(
            new DiscordFollowupMessageBuilder()
                .WithContent(CreatePrivateContent(service.GetPrivateSnapshot(tableId, e.Interaction.User.Id), 0))
                .AsEphemeral(true)
        );
        await UpdatePublicAsync(client, result.Game, service.GetSnapshot(tableId));
    }

    private static async Task ToggleCardAsync(ComponentInteractionCreateEventArgs e, PokerService service, string tableId, int cardIndex, int mask)
    {
        EnsureUser(e, service, tableId);
        var newMask = mask ^ (1 << cardIndex);
        await e.Interaction.CreateResponseAsync(
            InteractionResponseType.UpdateMessage,
            CreatePrivateBuilder(service.GetPrivateSnapshot(tableId, e.Interaction.User.Id), newMask)
        );
    }

    private static async Task ExchangeAsync(DiscordClient client, ComponentInteractionCreateEventArgs e, PokerService service, string tableId, int mask)
    {
        EnsureUser(e, service, tableId);
        var indexes = Enumerable.Range(0, 5).Where(index => (mask & (1 << index)) != 0).ToArray();
        var result = await service.ExchangeAsync(tableId, e.Interaction.User.Id, indexes);
        await e.Interaction.CreateResponseAsync(
            InteractionResponseType.UpdateMessage,
            CreatePrivateBuilder(service.GetPrivateSnapshot(tableId, e.Interaction.User.Id), 0)
        );
        await UpdatePublicAsync(client, result.Game, service.GetSnapshot(tableId));
    }

    private static async Task ActionAsync(DiscordClient client, ComponentInteractionCreateEventArgs e, PokerService service, string[] parts)
    {
        if (!long.TryParse(parts.ElementAtOrDefault(4), out var amount))
            amount = 0;
        EnsureUser(e, service, parts[2]);
        if (!Enum.TryParse<PokerAction>(parts[3], true, out var action))
            throw new InvalidOperationException("無効な操作です。");

        var result = await service.ActAsync(parts[2], e.Interaction.User.Id, action, amount);
        await e.Interaction.CreateResponseAsync(
            InteractionResponseType.UpdateMessage,
            CreatePrivateBuilder(service.GetPrivateSnapshot(parts[2], e.Interaction.User.Id), 0)
        );
        if (result.Finished)
            await service.SettleAsync(parts[2]);
        await UpdatePublicAsync(client, result.Game, service.GetSnapshot(parts[2]));
    }

    private static void EnsureUser(ComponentInteractionCreateEventArgs e, PokerService service, string tableId)
    {
        var game = service.Find(tableId) ?? throw new InvalidOperationException("指定された卓は存在しません。");
        if (game.Players.All(player => player.UserId != e.Interaction.User.Id))
            throw new InvalidOperationException("この卓の参加者ではありません。");
    }

    private static async Task ShowPrivateAsync(ComponentInteractionCreateEventArgs e, PokerService service, string tableId)
    {
        EnsureUser(e, service, tableId);
        await e.Interaction.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            CreatePrivateBuilder(service.GetPrivateSnapshot(tableId, e.Interaction.User.Id), 0).AsEphemeral(true)
        );
    }

    private static async Task UpdatePublicAsync(DiscordClient client, PokerGame game, PokerGameSnapshot snapshot)
    {
        if (!game.PublicMessageId.HasValue)
            return;
        var channel = await client.GetChannelAsync(game.ChannelId);
        var message = await channel.GetMessageAsync(game.PublicMessageId.Value);
        await message.ModifyAsync(CreatePublicMessageBuilder(snapshot, snapshot.Phase == PokerPhase.Waiting));
    }

    private static DiscordInteractionResponseBuilder CreatePublicBuilder(PokerGameSnapshot snapshot, bool includeJoin)
    {
        var builder = new DiscordInteractionResponseBuilder()
            .AddEmbed(CreatePublicEmbed(snapshot))
            .AddComponents(CreatePublicComponents(snapshot, includeJoin));
        return builder;
    }

    private static DiscordMessageBuilder CreatePublicMessageBuilder(PokerGameSnapshot snapshot, bool includeJoin)
        => new DiscordMessageBuilder()
            .AddEmbed(CreatePublicEmbed(snapshot))
            .AddComponents(CreatePublicComponents(snapshot, includeJoin));

    private static DiscordEmbed CreatePublicEmbed(PokerGameSnapshot snapshot)
    {
        var description = $"参加費：{snapshot.CoinRate:N0} Coin\n交換レート：1000 Chip = {snapshot.CoinRate:N0} Coin\n\n";
        description += snapshot.Players.Count == 0
            ? "参加者：まだいません"
            : string.Join("\n", snapshot.Players.Select(player => $"{player.DisplayName}：{player.Chips:N0} Chip"));
        description += $"\n\nPot：{snapshot.Pot:N0} Chip\nフェーズ：{GetPhaseText(snapshot.Phase, snapshot.BetRound)}";
        if (snapshot.CurrentPlayerId.HasValue)
        {
            var current = snapshot.Players.FirstOrDefault(player => player.UserId == snapshot.CurrentPlayerId.Value);
            description += $"\n現在の手番：{current?.DisplayName ?? "不明"}";
        }
        if (snapshot.Actions.Count > 0)
            description += "\n\n" + string.Join("\n", snapshot.Actions.TakeLast(12));
        if (!string.IsNullOrWhiteSpace(snapshot.WinnerText))
            description += $"\n\n勝者：{snapshot.WinnerText}";

        return new DiscordEmbedBuilder()
            .WithTitle($"🎴 5 Card Poker — {snapshot.TableId}")
            .WithDescription(description)
            .WithColor(DiscordColor.Blurple)
            .Build();
    }

    private static DiscordComponent[] CreatePublicComponents(PokerGameSnapshot snapshot, bool includeJoin)
    {
        var components = new List<DiscordComponent>
        {
            new DiscordButtonComponent(ButtonStyle.Primary, $"{Prefix}refresh:{snapshot.TableId}", "🔄 手札を再表示")
        };
        if (includeJoin)
            components.Add(new DiscordButtonComponent(ButtonStyle.Success, $"{Prefix}join:{snapshot.TableId}", "参加"));
        return components.ToArray();
    }

    private static DiscordInteractionResponseBuilder CreatePrivateBuilder(PokerPrivateSnapshot snapshot, int selectedMask)
    {
        var game = snapshot.Game;
        var player = snapshot.Player;
        var content = "🎴 あなたの手札\n\n" + string.Join("  ", player.Hand.Select((card, index) =>
            (selectedMask & (1 << index)) != 0 ? $"[{card}]" : card.ToString()));
        content += $"\n\nChip：{player.Chips:N0}\nフェーズ：{GetPhaseText(game.Phase, game.BetRound)}";
        var builder = new DiscordInteractionResponseBuilder().WithContent(content);

        if (game.Phase == PokerPhase.Exchange && !player.Exchanged && !player.Folded)
        {
            var buttons = player.Hand.Select((card, index) => (DiscordComponent)new DiscordButtonComponent(
                (selectedMask & (1 << index)) != 0 ? ButtonStyle.Success : ButtonStyle.Secondary,
                $"{Prefix}card:{game.TableId}:{player.UserId}:{index}:{selectedMask}",
                card.ToString())).ToArray();
            builder.AddComponents(buttons);
            builder.AddComponents(new DiscordComponent[]
            {
                new DiscordButtonComponent(ButtonStyle.Primary, $"{Prefix}exchange:{game.TableId}:{player.UserId}:{selectedMask}", "交換する")
            });
        }
        else if ((game.Phase is PokerPhase.BetRound1 or PokerPhase.BetRound2) && !player.Folded && !player.AllIn && game.CurrentPlayerIndex >= 0 && game.Players[game.CurrentPlayerIndex].UserId == player.UserId)
        {
            var components = new List<DiscordComponent>();
            if (game.CurrentBet == 0)
                components.Add(new DiscordButtonComponent(ButtonStyle.Secondary, $"{Prefix}action:{game.TableId}:Check:0", "Check"));
            else
                components.Add(new DiscordButtonComponent(ButtonStyle.Primary, $"{Prefix}action:{game.TableId}:Call:0", $"Call {game.CurrentBet - player.CurrentBet}"));
            if (game.CurrentBet == 0)
                components.Add(new DiscordButtonComponent(ButtonStyle.Primary, $"{Prefix}action:{game.TableId}:Bet:100", "Bet 100"));
            else
                components.Add(new DiscordButtonComponent(ButtonStyle.Primary, $"{Prefix}action:{game.TableId}:Raise:{game.CurrentBet + PokerService.MinimumRaise}", $"Raise {game.CurrentBet + PokerService.MinimumRaise}"));
            components.Add(new DiscordButtonComponent(ButtonStyle.Danger, $"{Prefix}action:{game.TableId}:Fold:0", "Fold"));
            components.Add(new DiscordButtonComponent(ButtonStyle.Success, $"{Prefix}action:{game.TableId}:AllIn:0", "All-in"));
            builder.AddComponents(components);
        }
        return builder;
    }

    private static string CreatePrivateContent(PokerPrivateSnapshot snapshot, int selectedMask)
    {
        var player = snapshot.Player;
        return "🎴 あなたの手札\n\n" + string.Join("  ", player.Hand.Select((card, index) =>
            (selectedMask & (1 << index)) != 0 ? $"[{card}]" : card.ToString()))
            + $"\n\nChip：{player.Chips:N0}\nフェーズ：{GetPhaseText(snapshot.Game.Phase, snapshot.Game.BetRound)}";
    }

    private static string GetPhaseText(PokerPhase phase, int betRound) => phase switch
    {
        PokerPhase.Waiting => "参加受付中",
        PokerPhase.Exchange => "カード交換",
        PokerPhase.Finished => "ゲーム終了",
        _ => $"Bet Round {betRound}"
    };

    private static Task RespondErrorAsync(InteractionContext ctx, string message)
        => ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent(message).AsEphemeral(true));

    private static Task RespondErrorAsync(ComponentInteractionCreateEventArgs e, string message)
        => e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent(message).AsEphemeral(true));
}
