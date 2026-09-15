using System;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services;

namespace TacoDiscordBot.Commands;

public sealed class CoinCommands : ApplicationCommandModule
{
    [SlashCommand("exchange", "VC滞在時間をコインに換金します")]
    public async Task Exchange(InteractionContext ctx)
    {
        if (ctx.Guild == null || BotHost.VcExchangeService == null)
        {
            await RespondAsync(ctx, "このコマンドはDB接続済みのサーバー内で利用できます。", true);
            return;
        }

        var preview = await BotHost.VcExchangeService.GetPreviewAsync(ctx.Guild.Id, ctx.User.Id);
        var builder = new DiscordInteractionResponseBuilder().AddEmbed(CreateExchangeEmbed(preview));
        if (preview.CanExchange)
        {
            builder.AddComponents(new DiscordComponent[]
            {
                new DiscordButtonComponent(ButtonStyle.Success, $"exchange:confirm:{ctx.Guild.Id}:{ctx.User.Id}", "💰 換金"),
                new DiscordButtonComponent(ButtonStyle.Secondary, $"exchange:cancel:{ctx.Guild.Id}:{ctx.User.Id}", "❌ キャンセル")
            });
        }

        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, builder);
    }

    [SlashCommand("pay", "ユーザーにコインを送信します")]
    public async Task Pay(
        InteractionContext ctx,
        [Option("user", "コインを送信するユーザー")] DiscordUser user,
        [Option("coin", "送信するコイン数")] long coin)
    {
        if (ctx.Guild == null || BotHost.CoinService == null)
        {
            await RespondAsync(ctx, "このコマンドはDB接続済みのサーバー内で利用できます。", true);
            return;
        }

        if (coin <= 0)
        {
            await RespondAsync(ctx, "❌ 送信するコインは1枚以上にしてください。", true);
            return;
        }

        if (user.Id == ctx.User.Id)
        {
            await RespondAsync(ctx, "❌ 自分自身にコインを送信することはできません。", true);
            return;
        }

        if (user.IsBot)
        {
            await RespondAsync(ctx, "❌ Botにコインを送信することはできません。", true);
            return;
        }

        var balanceBefore = await BotHost.CoinService.GetBalanceAsync(ctx.Guild.Id, ctx.User.Id);
        var transferred = await BotHost.CoinService.TransferAsync(ctx.Guild.Id, ctx.User.Id, user.Id, coin);
        if (!transferred)
        {
            await RespondAsync(ctx,
                $"❌ コインが不足しています。\n\n現在の残高：\n{balanceBefore:N0}コイン\n\n送信しようとしているコイン：\n{coin:N0}コイン",
                true);
            return;
        }

        var balanceAfter = await BotHost.CoinService.GetBalanceAsync(ctx.Guild.Id, ctx.User.Id);
        await RespondAsync(ctx,
            $"💰 コイン送信完了！\n\n<@{user.Id}> に {coin:N0}コインを送信しました。\n\n送信前残高：\n{balanceBefore:N0}コイン\n\n送信後残高：\n{balanceAfter:N0}コイン",
            false);
    }

    public static async Task HandleExchangeInteractionAsync(DiscordClient client, ComponentInteractionCreateEventArgs e)
    {
        var parts = e.Interaction.Data.CustomId?.Split(':');
        if (parts == null || parts.Length != 4 || parts[0] != "exchange"
            || !ulong.TryParse(parts[2], out var guildId)
            || !ulong.TryParse(parts[3], out var userId))
            return;

        if (e.Interaction.User.Id != userId)
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent("⚠️ この換金を操作できるのは実行者だけです。")
                    .AsEphemeral(true));
            return;
        }

        if (parts[1] == "cancel")
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder().WithContent("換金をキャンセルしました。"));
            return;
        }

        if (parts[1] != "confirm" || BotHost.VcExchangeService == null)
            return;

        // DB処理前にACKし、Discordの3秒制限を回避します。
        await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
        try
        {
            var result = await BotHost.VcExchangeService.ExchangeAsync(guildId, userId);
            var response = result == null
                ? new DiscordWebhookBuilder().WithContent("⚠️ 現在換金できる時間がありません。")
                : new DiscordWebhookBuilder().AddEmbed(CreateExchangeCompletedEmbed(result));
            await e.Interaction.EditOriginalResponseAsync(response);
        }
        catch (Exception ex)
        {
            await e.Interaction.CreateFollowupMessageAsync(
                new DiscordFollowupMessageBuilder()
                    .WithContent("換金処理中にエラーが発生しました。時間をおいて再度お試しください。")
                    .AsEphemeral(true));
            Console.Error.WriteLine($"[CoinCommands] VC換金エラー: {ex}");
        }
    }

    [SlashCommand("status", "自分のコインとVC滞在時間を表示します")]
    public async Task Status(InteractionContext ctx)
    {
        if (ctx.Guild == null || BotHost.CoinService == null)
        {
            await RespondAsync(ctx, "このコマンドはDB接続済みのサーバー内で利用できます。", true);
            return;
        }

        var balance = await BotHost.CoinService.GetBalanceAsync(ctx.Guild.Id, ctx.User.Id);
        var users = await BotHost.CoinService.GetRankingAsync(ctx.Guild.Id);
        var members = ctx.Guild.Members.Values.Where(member => !member.IsBot).Select(member => member.Id).ToHashSet();
        var ranking = users.Where(user => members.Contains(user.UserId)).ToList();
        var rank = ranking.FindIndex(user => user.UserId == ctx.User.Id) + 1;
        var seconds = BotHost.VcRankingService == null
            ? 0
            : await BotHost.VcRankingService.GetUserTotalSecondsAsync(ctx.Guild.Id, ctx.User.Id);
        var embed = new DiscordEmbedBuilder()
            .WithTitle("📊 STATUS")
            .WithDescription($"👤 ユーザー名\n{ctx.User.Username}\n\n💰 所有コイン\n{balance:N0}\n\n🎧 VC滞在時間\n{FormatDuration(seconds)}\n\n🏆 サーバーランキング\n{(rank > 0 ? $"{rank}位" : "圏外")}")
            .WithColor(DiscordColor.Blurple)
            .Build();
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().AddEmbed(embed));
    }

    [SlashCommand("richrank", "サーバー内のコインランキングを表示します")]
    public async Task RichRank(InteractionContext ctx)
    {
        if (ctx.Guild == null || BotHost.CoinService == null)
        {
            await RespondAsync(ctx, "このコマンドはDB接続済みのサーバー内で利用できます。", true);
            return;
        }

        var members = ctx.Guild.Members.Values.Where(member => !member.IsBot).Select(member => member.Id).ToHashSet();
        var ranking = (await BotHost.CoinService.GetRankingAsync(ctx.Guild.Id))
            .Where(user => members.Contains(user.UserId))
            .Take(10)
            .ToList();
        var lines = ranking.Count == 0
            ? "ランキング対象のユーザーがいません。"
            : string.Join("\n", ranking.Select((user, index) => $"{index + 1}位 <@{user.UserId}>\n    {user.Coins:N0} coins"));
        var embed = new DiscordEmbedBuilder().WithTitle("💰 RICH RANKING").WithDescription(lines).WithColor(DiscordColor.Gold).Build();
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().AddEmbed(embed));
    }

    private static DiscordEmbed CreateExchangeEmbed(VcExchangePreview preview)
    {
        var description = $"VC滞在時間：\n{FormatDuration(preview.TotalSeconds)}\n\n"
            + $"換金済み時間：\n{FormatDuration(preview.ExchangedSeconds)}\n\n"
            + $"換金可能時間：\n{FormatDuration(preview.AvailableSeconds)}\n\n"
            + $"換金レート：\n{VcExchangeSettings.ExchangeUnitMinutes}分 = {VcExchangeSettings.ExchangeCoinsPerUnit}コイン\n\n";
        if (!preview.CanExchange)
        {
            var remainingMinutes = preview.AvailableSeconds / 60;
            var minutesUntilExchange = VcExchangeSettings.ExchangeUnitMinutes - (remainingMinutes % VcExchangeSettings.ExchangeUnitMinutes);
            description += "⚠️ 現在換金できる時間がありません。\n\n"
                + $"あと{minutesUntilExchange}分VCに滞在すると{VcExchangeSettings.ExchangeCoinsPerUnit}コインに換金できます。";
        }
        else
        {
            description += $"換金対象：\n{FormatDuration(preview.ExchangeableSeconds)}\n\n"
                + $"獲得コイン：\n{preview.Coins:N0}コイン\n\n"
                + $"{FormatDuration(preview.ExchangeableSeconds)}を{preview.Coins:N0}コインに換金しますか？";
        }

        return new DiscordEmbedBuilder().WithTitle("💰 VC滞在時間 換金").WithDescription(description).WithColor(DiscordColor.Gold).Build();
    }

    private static DiscordEmbed CreateExchangeCompletedEmbed(VcExchangeResult result)
        => new DiscordEmbedBuilder()
            .WithTitle("💰 VC滞在時間 換金完了")
            .WithDescription($"{FormatDuration(result.Preview.ExchangeableSeconds)}を{result.Preview.Coins:N0}コインに換金しました。\n\n現在のコイン：{result.NewBalance:N0}コイン")
            .WithColor(DiscordColor.Green)
            .Build();

    private static string FormatDuration(long seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(long)span.TotalHours}時間 {span.Minutes}分";
    }

    private static Task RespondAsync(InteractionContext ctx, string message, bool ephemeral)
        => ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(message).AsEphemeral(ephemeral));
}
