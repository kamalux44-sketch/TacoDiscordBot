using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Commands;

public sealed class PokerCommands : ApplicationCommandModule
{
    private const string Prefix = "poker:";
    private static readonly SemaphoreSlim RestoredGamesSyncLock = new(1, 1);

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
        await RestoredGamesSyncLock.WaitAsync();
        try
        {
            foreach (var game in service.GetGames())
            {
                try
                {
                    var snapshot = service.GetSnapshot(game.TableId);
                    var channel = await client.GetChannelAsync(game.ChannelId);
                    DiscordMessage? message = null;

                    if (game.PublicMessageId.HasValue)
                    {
                        try
                        {
                            message = await channel.GetMessageAsync(game.PublicMessageId.Value);
                        }
                        catch (DSharpPlus.Exceptions.NotFoundException)
                        {
                            message = null;
                        }
                    }

                    if (message == null)
                    {
                        await service.CloseDueToMissingPublicMessageAsync(game.TableId);
                    }
                    else
                    {
                        await UpdateMessagesAsync(client, service, game, snapshot);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Poker卓の公開メッセージ再同期に失敗しました。table={TableId}", game.TableId);
                }
            }
        }
        finally
        {
            RestoredGamesSyncLock.Release();
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

        var operation = parts.ElementAtOrDefault(1);
        var deferred = false;
        try
        {
            if (operation is "join" or "start" or "card" or "exchange" or "action")
            {
                await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
                deferred = true;
            }
            switch (parts.ElementAtOrDefault(1))
            {
                case "join" when parts.Length == 3:
                    await JoinAsync(client, e, service, parts[2]);
                    break;
                case "start" when parts.Length == 3:
                    await StartAsync(client, e, service, parts[2]);
                    break;
                case "refresh" when parts.Length == 3:
                    await ShowPrivateAsync(e, service, parts[2]);
                    break;
                case "bet" or "raise" when parts.Length == 3:
                    await ShowBettingModalAsync(e, parts[1], parts[2]);
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
            await RespondComponentErrorAsync(e, ex.Message, deferred, operation == "refresh");
        }
        catch (InvalidOperationException ex)
        {
            await RespondComponentErrorAsync(e, ex.Message, deferred, operation == "refresh");
        }
    }

    public static async Task HandleModalSubmitAsync(DiscordClient client, ModalSubmitEventArgs e)
    {
        var customId = e.Interaction.Data.CustomId;
        if (string.IsNullOrWhiteSpace(customId) || !customId.StartsWith(Prefix, StringComparison.Ordinal))
            return;

        var parts = customId.Split(':');
        var operation = parts.ElementAtOrDefault(1);
        if (operation is not ("bet" or "raise") || parts.Length != 3)
            return;

        var service = BotHost.PokerService;
        if (service == null)
            return;

        var deferred = false;
        try
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.DeferredChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().AsEphemeral(true));
            deferred = true;

            EnsureUser(service, parts[2], e.Interaction.User.Id);
            if (!e.Values.TryGetValue("amount", out var amountText)
                || !long.TryParse(amountText, out var amount))
            {
                throw new InvalidOperationException("Bet/Raise額には整数を入力してください。");
            }

            var action = operation == "bet" ? PokerAction.Bet : PokerAction.Raise;
            var result = await service.ActAsync(parts[2], e.Interaction.User.Id, action, amount);
            if (result.Finished)
            {
                var finalPrivateSnapshot = service.GetPrivateSnapshot(parts[2], e.Interaction.User.Id);
                await e.Interaction.EditOriginalResponseAsync(
                    new DiscordWebhookBuilder().WithContent(CreatePrivateContent(finalPrivateSnapshot, 0)));
                var finalSnapshot = service.GetSnapshot(parts[2]);
                try
                {
                    await UpdateMessagesAsync(client, service, result.Game, finalSnapshot);
                }
                finally
                {
                    await service.SettleAsync(parts[2]);
                }
                return;
            }

            var privateSnapshot = service.GetPrivateSnapshot(parts[2], e.Interaction.User.Id);
            var actionText = operation == "bet" ? "Betしました。" : "レイズしました。";
            var privateBuilder = new DiscordWebhookBuilder()
                .WithContent($"{actionText}\n\n" + CreatePrivateContent(privateSnapshot, 0));
            AddPrivateComponents(privateBuilder, privateSnapshot, 0);
            await e.Interaction.EditOriginalResponseAsync(privateBuilder);

            await UpdateMessagesAsync(client, service, result.Game, service.GetSnapshot(parts[2]));
        }
        catch (ArgumentException ex)
        {
            await RespondModalErrorAsync(e, ex.Message, deferred);
        }
        catch (InvalidOperationException ex)
        {
            await RespondModalErrorAsync(e, ex.Message, deferred);
        }
    }

    private static async Task JoinAsync(DiscordClient client, ComponentInteractionCreateEventArgs e, PokerService service, string tableId)
    {
        if (e.Guild == null)
            throw new InvalidOperationException("サーバー内でのみ参加できます。");

        var result = await service.JoinAsync(tableId, e.Interaction.User.Id, e.Interaction.User.Username);
        await e.Interaction.EditOriginalResponseAsync(
            CreatePublicWebhookBuilder(service.GetSnapshot(tableId), includeJoin: true));
        await UpdateMessagesAsync(client, service, result.Game, service.GetSnapshot(tableId));
    }

    private static async Task StartAsync(DiscordClient client, ComponentInteractionCreateEventArgs e, PokerService service, string tableId)
    {
        var game = await service.StartAsync(tableId, e.Interaction.User.Id);
        await e.Interaction.EditOriginalResponseAsync(
            CreatePublicWebhookBuilder(service.GetSnapshot(tableId), includeJoin: false));

        await UpdateMessagesAsync(client, service, game, service.GetSnapshot(tableId));
    }

    private static async Task ToggleCardAsync(ComponentInteractionCreateEventArgs e, PokerService service, string tableId, int cardIndex, int mask)
    {
        EnsureUser(service, tableId, e.Interaction.User.Id);
        var newMask = mask ^ (1 << cardIndex);
        await e.Interaction.EditOriginalResponseAsync(
            CreatePrivateWebhookBuilder(service.GetPrivateSnapshot(tableId, e.Interaction.User.Id), newMask));
    }

    private static async Task ExchangeAsync(DiscordClient client, ComponentInteractionCreateEventArgs e, PokerService service, string tableId, int mask)
    {
        EnsureUser(service, tableId, e.Interaction.User.Id);
        var indexes = Enumerable.Range(0, 5).Where(index => (mask & (1 << index)) != 0).ToArray();
        var result = await service.ExchangeAsync(tableId, e.Interaction.User.Id, indexes);
        await e.Interaction.EditOriginalResponseAsync(
            CreatePrivateWebhookBuilder(service.GetPrivateSnapshot(tableId, e.Interaction.User.Id), 0));
        await UpdateMessagesAsync(client, service, result.Game, service.GetSnapshot(tableId));
    }

    private static async Task ActionAsync(DiscordClient client, ComponentInteractionCreateEventArgs e, PokerService service, string[] parts)
    {
        if (!long.TryParse(parts.ElementAtOrDefault(4), out var amount))
            amount = 0;
        EnsureUser(service, parts[2], e.Interaction.User.Id);
        if (!Enum.TryParse<PokerAction>(parts[3], true, out var action))
            throw new InvalidOperationException("無効な操作です。");

        var result = await service.ActAsync(parts[2], e.Interaction.User.Id, action, amount);
        await e.Interaction.EditOriginalResponseAsync(
            CreatePrivateWebhookBuilder(service.GetPrivateSnapshot(parts[2], e.Interaction.User.Id), 0));
        if (result.Finished)
        {
            var finalSnapshot = service.GetSnapshot(parts[2]);
            try
            {
                await UpdateMessagesAsync(client, service, result.Game, finalSnapshot);
            }
            finally
            {
                await service.SettleAsync(parts[2]);
            }
            return;
        }
        await UpdateMessagesAsync(client, service, result.Game, service.GetSnapshot(parts[2]));
    }

    private static async Task ShowBettingModalAsync(ComponentInteractionCreateEventArgs e, string operation, string tableId)
    {
        // モーダルはコンポーネント interaction の初回応答として直接表示する。
        await e.Interaction.CreateResponseAsync(
            InteractionResponseType.Modal,
            new DiscordInteractionResponseBuilder()
                .WithCustomId($"{Prefix}{operation}:{tableId}")
                .WithTitle(operation == "bet" ? "Bet額を入力" : "レイズ額を入力")
                .AddComponents(new TextInputComponent(
                    operation == "bet" ? "Bet額" : "レイズ後の合計ベット額",
                    "amount",
                    operation == "bet" ? "例：100" : "例：300",
                    null,
                    true,
                    TextInputStyle.Short,
                    1,
                    18)));
    }

    private static void EnsureUser(PokerService service, string tableId, ulong userId)
    {
        var game = service.Find(tableId) ?? throw new InvalidOperationException("指定された卓は存在しません。");
        if (game.Players.All(player => player.UserId != userId))
            throw new InvalidOperationException("この卓の参加者ではありません。");
    }

    private static async Task ShowPrivateAsync(ComponentInteractionCreateEventArgs e, PokerService service, string tableId)
    {
        EnsureUser(service, tableId, e.Interaction.User.Id);
        var game = service.Find(tableId) ?? throw new InvalidOperationException("指定された卓は存在しません。");
        if (game.Phase is PokerPhase.BetRound1 or PokerPhase.BetRound2
            && (game.CurrentPlayerIndex < 0 || game.Players[game.CurrentPlayerIndex].UserId != e.Interaction.User.Id))
        {
            throw new InvalidOperationException("現在の手番ではありません。");
        }

        var snapshot = service.GetPrivateSnapshot(tableId, e.Interaction.User.Id);
        var builder = new DiscordInteractionResponseBuilder()
            .WithContent(CreatePrivateContent(snapshot, 0));
        AddPrivateComponents(builder, snapshot, 0);
        await e.Interaction.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            builder.AsEphemeral(true));
    }

    private static async Task UpdateMessagesAsync(
        DiscordClient client,
        PokerService service,
        PokerGame game,
        PokerGameSnapshot snapshot)
    {
        if (!game.PublicMessageId.HasValue)
            return;

        var channel = await client.GetChannelAsync(game.ChannelId);
        var message = await channel.GetMessageAsync(game.PublicMessageId.Value);
        await message.ModifyAsync(CreatePublicMessageBuilder(snapshot, snapshot.Phase == PokerPhase.Waiting));

        DiscordGuild? guild = null;
        foreach (var player in game.Players)
        {
            try
            {
                DiscordChannel dmChannel;
                if (player.DirectMessageChannelId.HasValue)
                {
                    dmChannel = await client.GetChannelAsync(player.DirectMessageChannelId.Value);
                }
                else
                {
                    guild ??= await client.GetGuildAsync(game.GuildId);
                    var member = await guild.GetMemberAsync(player.UserId);
                    dmChannel = await member.CreateDmChannelAsync();
                }

                DiscordMessage? directPublicMessage = null;
                var directPublicMessageId = player.DirectPublicMessageId;
                if (!directPublicMessageId.HasValue)
                {
                    directPublicMessage = await dmChannel.SendMessageAsync(
                        CreateDirectPublicMessageBuilder(snapshot));
                    directPublicMessageId = directPublicMessage.Id;
                }
                else
                {
                    directPublicMessage = await GetMessageOrNullAsync(dmChannel, directPublicMessageId);
                    if (directPublicMessage != null)
                    {
                        await directPublicMessage.ModifyAsync(CreateDirectPublicMessageBuilder(snapshot));
                    }
                }

                DiscordMessage? handMessage = null;
                var handMessageId = player.DirectHandMessageId;
                if (!handMessageId.HasValue)
                {
                    handMessage = await dmChannel.SendMessageAsync(
                        CreatePrivateMessageBuilder(
                            service.GetPrivateSnapshot(game.TableId, player.UserId),
                            0));
                    handMessageId = handMessage.Id;
                }
                else
                {
                    handMessage = await GetMessageOrNullAsync(dmChannel, handMessageId);
                    if (handMessage != null)
                    {
                        await handMessage.ModifyAsync(
                            CreatePrivateMessageBuilder(
                                service.GetPrivateSnapshot(game.TableId, player.UserId),
                                0));
                    }
                }

                if (player.DirectMessageChannelId != dmChannel.Id
                    || player.DirectPublicMessageId != directPublicMessageId
                    || player.DirectHandMessageId != handMessageId)
                {
                    await service.SetPlayerDirectMessageIdsAsync(
                        game.TableId,
                        player.UserId,
                        dmChannel.Id,
                        directPublicMessageId ?? 0,
                        handMessageId ?? 0);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Poker参加者DMの同期に失敗しました。user={UserId} table={TableId}", player.UserId, game.TableId);
            }
        }
    }

    private static async Task<DiscordMessage?> GetMessageOrNullAsync(
        DiscordChannel channel,
        ulong? messageId)
    {
        if (!messageId.HasValue)
            return null;

        try
        {
            return await channel.GetMessageAsync(messageId.Value);
        }
        catch (DSharpPlus.Exceptions.NotFoundException)
        {
            return null;
        }
    }

    private static DiscordInteractionResponseBuilder CreatePublicBuilder(PokerGameSnapshot snapshot, bool includeJoin)
    {
        var builder = new DiscordInteractionResponseBuilder()
            .AddEmbed(CreatePublicEmbed(snapshot));
        AddPublicComponents(builder, snapshot, includeJoin);
        return builder;
    }

    private static DiscordMessageBuilder CreatePublicMessageBuilder(PokerGameSnapshot snapshot, bool includeJoin)
    {
        var builder = new DiscordMessageBuilder()
            .AddEmbed(CreatePublicEmbed(snapshot));
        AddPublicComponents(builder, snapshot, includeJoin);
        return builder;
    }

    private static DiscordMessageBuilder CreateDirectPublicMessageBuilder(PokerGameSnapshot snapshot)
        => new DiscordMessageBuilder()
            .AddEmbed(CreatePublicEmbed(snapshot));

    private static DiscordWebhookBuilder CreatePublicWebhookBuilder(PokerGameSnapshot snapshot, bool includeJoin)
    {
        var builder = new DiscordWebhookBuilder()
            .AddEmbed(CreatePublicEmbed(snapshot));
        AddPublicComponents(builder, snapshot, includeJoin);
        return builder;
    }

    private static void AddPublicComponents(
        DiscordInteractionResponseBuilder builder,
        PokerGameSnapshot snapshot,
        bool includeJoin)
    {
        var components = CreatePublicComponents(snapshot, includeJoin);
        if (components.Length > 0)
            builder.AddComponents(components);
    }

    private static void AddPublicComponents(
        DiscordMessageBuilder builder,
        PokerGameSnapshot snapshot,
        bool includeJoin)
    {
        var components = CreatePublicComponents(snapshot, includeJoin);
        if (components.Length > 0)
            builder.AddComponents(components);
    }

    private static void AddPublicComponents(
        DiscordWebhookBuilder builder,
        PokerGameSnapshot snapshot,
        bool includeJoin)
    {
        var components = CreatePublicComponents(snapshot, includeJoin);
        if (components.Length > 0)
            builder.AddComponents(components);
    }

    private static DiscordEmbed CreatePublicEmbed(PokerGameSnapshot snapshot)
    {
        var description = $"💰 Pot\n{snapshot.Pot:N0} Chip\n\n👥 Players\n";
        description += snapshot.Players.Count == 0
            ? "参加者：まだいません"
            : string.Join("\n", snapshot.Players.Select(player => $"{player.DisplayName} : {player.Chips:N0} Chip"));
        description += $"\n\n📍 Status\nフェーズ : {GetPhaseText(snapshot.Phase, snapshot.BetRound)}";

        if (snapshot.CurrentPlayerId.HasValue)
        {
            var current = snapshot.Players.FirstOrDefault(player => player.UserId == snapshot.CurrentPlayerId.Value);
            description += $"\n現在の手番 : {current?.DisplayName ?? "不明"}";
        }

        AppendActionSections(ref description, snapshot);

        if (snapshot.Phase == PokerPhase.Finished)
        {
            description += "\n\n🃏 Showdown\n";
            description += string.Join("\n", snapshot.Players.Select(FormatShowdownPlayer));
            if (!string.IsNullOrWhiteSpace(snapshot.WinnerText))
                description += $"\n\n🏆 Winner\n{snapshot.WinnerText.Replace("（", "\n").Replace("）", string.Empty)}";
        }

        return new DiscordEmbedBuilder()
            .WithTitle($"♠ 5 Card Poker — {snapshot.TableId}")
            .WithDescription(description)
            .WithColor(DiscordColor.Blurple)
            .Build();
    }

    private static void AppendActionSections(ref string description, PokerGameSnapshot snapshot)
    {
        var exchangeIndex = snapshot.Actions.ToList().FindIndex(action => action.Contains("枚交換", StringComparison.Ordinal));
        var bettingActions = exchangeIndex < 0 ? snapshot.Actions : snapshot.Actions.Take(exchangeIndex).ToArray();
        var exchangeActions = snapshot.Actions.Where(action => action.Contains("枚交換", StringComparison.Ordinal)).ToArray();
        var finalActions = exchangeIndex < 0 ? Array.Empty<string>() : snapshot.Actions.Skip(exchangeIndex + 1).Where(action => !action.Contains("枚交換", StringComparison.Ordinal)).ToArray();

        AppendActionSection(ref description, "📜 Action Log", bettingActions);
        AppendActionSection(ref description, "🔄 Draw Phase", exchangeActions);
        AppendActionSection(ref description, "🎯 Final Round", finalActions);
    }

    private static void AppendActionSection(ref string description, string title, IEnumerable<string> actions)
    {
        var values = actions.TakeLast(12).Select(action => $"▶ {action.Replace("：", " ")}").ToArray();
        description += $"\n\n{title}\n" + (values.Length == 0 ? "なし" : string.Join("\n", values));
    }

    private static string FormatShowdownPlayer(PokerPlayerSnapshot player)
    {
        var hand = player.Hand.Count == 0 ? "非公開" : string.Join(" ", player.Hand);
        var category = player.HandCategory ?? "役なし";
        var folded = player.Folded ? "（Fold）" : string.Empty;
        return $"{player.DisplayName}{folded}\n手札 : {hand}\n役 : {category}\nチップ : {player.Chips:N0} Chip";
    }

    private static DiscordComponent[] CreatePublicComponents(PokerGameSnapshot snapshot, bool includeJoin)
    {
        var components = new List<DiscordComponent>();
        if (snapshot.Phase == PokerPhase.Waiting)
        {
            if (includeJoin)
                components.Add(new DiscordButtonComponent(ButtonStyle.Success, $"{Prefix}join:{snapshot.TableId}", "参加"));
            if (snapshot.Players.Count >= PokerService.MinPlayers)
                components.Add(new DiscordButtonComponent(ButtonStyle.Primary, $"{Prefix}start:{snapshot.TableId}", "開始"));
        }
        return components.ToArray();
    }

    private static DiscordWebhookBuilder CreatePrivateWebhookBuilder(PokerPrivateSnapshot snapshot, int selectedMask)
    {
        var builder = new DiscordWebhookBuilder()
            .WithContent(CreatePrivateContent(snapshot, selectedMask));
        AddPrivateComponents(builder, snapshot, selectedMask);
        return builder;
    }

    private static DiscordMessageBuilder CreatePrivateMessageBuilder(PokerPrivateSnapshot snapshot, int selectedMask)
    {
        var builder = new DiscordMessageBuilder()
            .WithContent(CreatePrivateContent(snapshot, selectedMask));
        AddPrivateComponents(builder, snapshot, selectedMask);
        return builder;
    }

    private static DiscordFollowupMessageBuilder CreatePrivateFollowupBuilder(PokerPrivateSnapshot snapshot, int selectedMask)
    {
        var builder = new DiscordFollowupMessageBuilder()
            .WithContent(CreatePrivateContent(snapshot, selectedMask));
        AddPrivateComponents(builder, snapshot, selectedMask);
        return builder;
    }

    private static void AddPrivateComponents(
        DiscordInteractionResponseBuilder builder,
        PokerPrivateSnapshot snapshot,
        int selectedMask)
    {
        AddPrivateComponentRows(CreatePrivateComponents(snapshot, selectedMask), row => { builder.AddComponents(row); });
    }

    private static void AddPrivateComponents(
        DiscordMessageBuilder builder,
        PokerPrivateSnapshot snapshot,
        int selectedMask)
    {
        AddPrivateComponentRows(CreatePrivateComponents(snapshot, selectedMask), row => { builder.AddComponents(row); });
    }

    private static void AddPrivateComponents(
        DiscordWebhookBuilder builder,
        PokerPrivateSnapshot snapshot,
        int selectedMask)
    {
        AddPrivateComponentRows(CreatePrivateComponents(snapshot, selectedMask), row => { builder.AddComponents(row); });
    }

    private static void AddPrivateComponents(
        DiscordFollowupMessageBuilder builder,
        PokerPrivateSnapshot snapshot,
        int selectedMask)
    {
        AddPrivateComponentRows(CreatePrivateComponents(snapshot, selectedMask), row => { builder.AddComponents(row); });
    }

    private static void AddPrivateComponentRows(
        DiscordComponent[] components,
        Action<DiscordComponent[]> addRow)
    {
        foreach (var row in components.Chunk(5))
            addRow(row);
    }

    private static DiscordComponent[] CreatePrivateComponents(PokerPrivateSnapshot snapshot, int selectedMask)
    {
        var game = snapshot.Game;
        var player = snapshot.Player;
        if (game.Phase == PokerPhase.Exchange && !player.Exchanged && !player.Folded)
        {
            var buttons = player.Hand.Select((card, index) => (DiscordComponent)new DiscordButtonComponent(
                (selectedMask & (1 << index)) != 0 ? ButtonStyle.Success : ButtonStyle.Secondary,
                $"{Prefix}card:{game.TableId}:{player.UserId}:{index}:{selectedMask}", card.ToString())).ToList();
            buttons.Add(new DiscordButtonComponent(ButtonStyle.Primary,
                $"{Prefix}exchange:{game.TableId}:{player.UserId}:{selectedMask}", "交換する"));
            return buttons.ToArray();
        }

        if (game.Phase is not (PokerPhase.BetRound1 or PokerPhase.BetRound2)
            || player.Folded || player.AllIn
            || game.CurrentPlayerIndex < 0
            || game.Players[game.CurrentPlayerIndex].UserId != player.UserId)
            return Array.Empty<DiscordComponent>();

        var components = new List<DiscordComponent>();
        if (game.CurrentBet == 0)
            components.Add(new DiscordButtonComponent(ButtonStyle.Secondary, $"{Prefix}action:{game.TableId}:Check:0", "Check"));
        else
            components.Add(new DiscordButtonComponent(ButtonStyle.Primary, $"{Prefix}action:{game.TableId}:Call:0", $"Call {game.CurrentBet - player.CurrentBet}"));
        if (game.CurrentBet == 0)
            components.Add(new DiscordButtonComponent(ButtonStyle.Primary, $"{Prefix}bet:{game.TableId}", "Bet"));
        else
            components.Add(new DiscordButtonComponent(ButtonStyle.Primary, $"{Prefix}raise:{game.TableId}", "Raise"));
        components.Add(new DiscordButtonComponent(ButtonStyle.Danger, $"{Prefix}action:{game.TableId}:Fold:0", "Fold"));
        components.Add(new DiscordButtonComponent(ButtonStyle.Success, $"{Prefix}action:{game.TableId}:AllIn:0", "All-in"));
        return components.ToArray();
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

    private static Task RespondModalErrorAsync(ModalSubmitEventArgs e, string message, bool deferred)
        => deferred
            ? e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithContent(message))
            : e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent(message).AsEphemeral(true));

    private static Task RespondComponentErrorAsync(
        ComponentInteractionCreateEventArgs e,
        string message,
        bool deferred,
        bool isEphemeral)
    {
        if (!deferred)
            return RespondErrorAsync(e, message);

        if (isEphemeral)
            return e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithContent(message));

        return e.Interaction.CreateFollowupMessageAsync(
            new DiscordFollowupMessageBuilder().WithContent(message).AsEphemeral(true));
    }
}
