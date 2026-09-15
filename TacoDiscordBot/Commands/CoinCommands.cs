using System;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;

namespace TacoDiscordBot.Commands;

public sealed class CoinCommands : ApplicationCommandModule
{
    [SlashCommand("status", "自分のコインとVC滞在時間を表示します")]
    public async Task Status(InteractionContext ctx)
    {
        if (ctx.Guild == null || BotHost.CoinService == null)
        {
            await RespondAsync(ctx, "このコマンドはDB接続済みのサーバー内で利用できます。", true);
            return;
        }

        // status実行時にユーザーデータを作成し、初期コインを確定させます。
        var balance = await BotHost.CoinService.GetBalanceAsync(ctx.User.Id);
        var users = await BotHost.CoinService.GetRankingAsync();
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
        var ranking = (await BotHost.CoinService.GetRankingAsync())
            .Where(user => members.Contains(user.UserId))
            .Take(10)
            .ToList();
        var lines = ranking.Count == 0
            ? "ランキング対象のユーザーがいません。"
            : string.Join("\n", ranking.Select((user, index) => $"{index + 1}位 <@{user.UserId}>\n    {user.Coins:N0} coins"));
        var embed = new DiscordEmbedBuilder().WithTitle("💰 RICH RANKING").WithDescription(lines).WithColor(DiscordColor.Gold).Build();
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().AddEmbed(embed));
    }

    private static string FormatDuration(long seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(long)span.TotalHours}時間 {span.Minutes}分";
    }

    private static Task RespondAsync(InteractionContext ctx, string message, bool ephemeral)
        => ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent(message).AsEphemeral(ephemeral));
}
