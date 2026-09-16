using System;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Services;

namespace TacoDiscordBot.Commands;

public sealed class CoinCommands : ApplicationCommandModule
{
    private const int RichRankingLimit = 10;
    private const int CoinAmountWidth = 9;
    private const int BankruptcyCountWidth = 2;

    [SlashCommand("exchange", "VC滞在時間をコインへ換金します")]
    public async Task Exchange(InteractionContext ctx)
    {
        if (ctx.Guild == null || BotHost.VcExchangeService == null)
        {
            await RespondAsync(ctx, "このコマンドはDB接続済みのサーバー内で利用できます。", true);
            return;
        }

        try
        {
            var summary = await BotHost.VcExchangeService.GetSummaryAsync(ctx.Guild.Id, ctx.User.Id);
            var exchangeableMinutes = VcExchangeService.GetExchangeableMinutes(summary);
            var coins = VcExchangeService.GetExchangeCoins(summary);
            if (exchangeableMinutes == 0)
            {
                var shortage = VcExchangeCalculator.ExchangeUnitMinutes - summary.UnexchangedSeconds / 60 % VcExchangeCalculator.ExchangeUnitMinutes;
                await RespondAsync(
                    ctx,
                    summary.UnexchangedSeconds == 0
                        ? "❌ 現在、換金できるVC滞在時間がありません。"
                        : $"❌ 換金できる時間がありません。\n\nあと{shortage}分VCに滞在すると25コインに換金できます。",
                    true
                );
                return;
            }

            await ctx.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                CreateExchangeBuilder(ctx.Guild.Id, ctx.User.Id, summary, exchangeableMinutes, coins)
            );
        }
        catch
        {
            await RespondAsync(ctx, "❌ VC滞在時間の取得中にエラーが発生しました。", true);
        }
    }

    public static async Task HandleExchangeComponentInteractionAsync(
        DiscordClient client,
        ComponentInteractionCreateEventArgs e
    )
    {
        var customId = e.Interaction.Data.CustomId;
        if (string.IsNullOrWhiteSpace(customId) || !customId.StartsWith("exchange:", StringComparison.Ordinal))
            return;

        var parts = customId.Split(':');
        if (parts.Length != 4 || !ulong.TryParse(parts[2], out var guildId) || !ulong.TryParse(parts[3], out var userId))
            return;

        if (e.Interaction.User.Id != userId)
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("⚠️ この換金画面を操作できるのは実行者だけです。").AsEphemeral(true)
            );
            return;
        }

        if (parts[1] == "cancel")
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder().WithContent("❌ 換金をキャンセルしました。")
            );
            return;
        }

        if (parts[1] != "confirm" || BotHost.VcExchangeService == null)
            return;

        try
        {
            var result = await BotHost.VcExchangeService.ExchangeAsync(guildId, userId);
            var message = result.Success
                ? $"✅ 換金完了！\n\n{FormatMinutes(result.ExchangeMinutes)}のVC滞在時間を換金しました。\n\n💰 +{result.Coins:N0}コイン\n\n残りの未換金時間：\n{FormatMinutes(result.RemainingMinutes)}"
                : "❌ このVC滞在時間はすでに換金済みです。";

            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder().WithContent(message).AddComponents(
                    new DiscordComponent[]
                    {
                        new DiscordButtonComponent(ButtonStyle.Success, customId, "💰 換金", true),
                        new DiscordButtonComponent(ButtonStyle.Secondary, $"exchange:cancel:{guildId}:{userId}", "キャンセル", true)
                    }
                )
            );
        }
        catch
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("❌ VC滞在時間の換金中にエラーが発生しました。\n\nコインと滞在時間は変更されていません。").AsEphemeral(true)
            );
        }
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

    [SlashCommand("status", "自分のコイン、破産回数、VC滞在時間を表示します")]
    public async Task Status(InteractionContext ctx)
    {
        if (ctx.Guild == null || BotHost.CoinService == null)
        {
            await RespondAsync(ctx, "このコマンドはDB接続済みのサーバー内で利用できます。", true);
            return;
        }

        var balance = await BotHost.CoinService.GetBalanceAsync(ctx.Guild.Id, ctx.User.Id);
        var userData = await BotHost.CoinService.GetUserDataAsync(ctx.Guild.Id, ctx.User.Id);
        var users = await BotHost.CoinService.GetRankingAsync(ctx.Guild.Id);
        var members = ctx.Guild.Members.Values.Where(member => !member.IsBot).Select(member => member.Id).ToHashSet();
        var ranking = users.Where(user => members.Contains(user.UserId)).ToList();
        var rank = ranking.FindIndex(user => user.UserId == ctx.User.Id) + 1;
        var seconds = BotHost.VcRankingService == null
            ? 0
            : await BotHost.VcRankingService.GetUserTotalSecondsAsync(ctx.Guild.Id, ctx.User.Id);
        var exchangeSummary = BotHost.VcExchangeService == null
            ? new TacoDiscordBot.Models.VcExchangeSummary(seconds, 0)
            : await BotHost.VcExchangeService.GetSummaryAsync(ctx.Guild.Id, ctx.User.Id);
        var exchangeableMinutes = VcExchangeService.GetExchangeableMinutes(exchangeSummary);
        var embed = new DiscordEmbedBuilder()
            .WithTitle("📊 STATUS")
            .WithDescription($"🏆 サーバーランキング\n{(rank > 0 ? $"{rank}位" : "圏外")}\n\n👤 ユーザー名\n{ctx.User.Username}\n\n💰 所有コイン\n{balance:N0}\n\n🎧 VC滞在時間\n{FormatDuration(seconds)}\n\n🔄 換金可能時間\n{FormatMinutes(exchangeableMinutes)}\n\n💀 破産回数\n{userData.LastChanceCount}回")
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

        var ranking = await BotHost.CoinService.GetTopRankingAsync(ctx.Guild.Id, RichRankingLimit);
        var lines = ranking.Count == 0
            ? "ランキング対象のユーザーがいません。"
            : string.Join("\n", ranking.Select((user, index) =>
                $"{index + 1}位 <@{user.UserId}>\n    {user.Coins.ToString("N0").PadLeft(CoinAmountWidth)} coins　 破産: {user.LastChanceCount.ToString("N0").PadLeft(BankruptcyCountWidth)}回"));
        var embed = new DiscordEmbedBuilder().WithTitle("💰 RICH RANKING").WithDescription(lines).WithColor(DiscordColor.Gold).Build();
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().AddEmbed(embed));
    }

    private static string FormatDuration(long seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(long)span.TotalHours}時間 {span.Minutes}分";
    }

    private static string FormatMinutes(long minutes)
        => FormatDuration(Math.Max(0, minutes) * 60);

    private static DiscordInteractionResponseBuilder CreateExchangeBuilder(
        ulong guildId,
        ulong userId,
        TacoDiscordBot.Models.VcExchangeSummary summary,
        long exchangeableMinutes,
        long coins
    )
    {
        var description = $"💰 VC滞在時間の換金\n\n" +
            $"VC滞在時間：\n{FormatDuration(summary.TotalSeconds)}\n\n" +
            $"換金済み時間：\n{FormatDuration(summary.ExchangedSeconds)}\n\n" +
            $"換金可能時間：\n{FormatMinutes(exchangeableMinutes)}\n\n" +
            $"換金レート：\n6分 = 25コイン\n\n" +
            $"今回換金：\n{FormatMinutes(exchangeableMinutes)}\n\n" +
            $"獲得コイン：\n{coins:N0}コイン\n\n" +
            $"{coins:N0}コインに換金しますか？";

        return new DiscordInteractionResponseBuilder()
            .WithContent(description)
            .AddComponents(
                new DiscordComponent[]
                {
                    new DiscordButtonComponent(ButtonStyle.Success, $"exchange:confirm:{guildId}:{userId}", "💰 換金"),
                    new DiscordButtonComponent(ButtonStyle.Secondary, $"exchange:cancel:{guildId}:{userId}", "キャンセル")
                }
            );
    }

    private static Task RespondAsync(InteractionContext ctx, string message, bool ephemeral)
        => ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(message).AsEphemeral(ephemeral));
}
