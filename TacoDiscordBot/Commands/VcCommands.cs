using System.Threading.Tasks;
using DSharpPlus.SlashCommands;
using DSharpPlus.Entities;
using TacoDiscordBot.Repository;
using System.Text;
using System.Threading.Tasks;

namespace TacoDiscordBot.Commands;

public class VcCommands : ApplicationCommandModule
{
    [SlashCommand("vclog", "Toggle VC join/leave/move logging to this text channel")]
    public async Task VcLog(InteractionContext ctx)
    {
        // Per-guild vc log targets supported. Toggle or set target for this guild.
        var guildId = ctx.Guild?.Id ?? 0UL;

        if (guildId == 0)
        {
            await ctx.Channel.SendMessageAsync("このコマンドはサーバー内で実行してください。");
            return;
        }

        // If a target exists for this guild, remove it (toggle off). Otherwise set this channel as target.
        if (BotHost.VcLogger.IsConfiguredForGuild(guildId))
        {
            await BotHost.VcLogger.RemoveChannelAsync(guildId);
            await ctx.Channel.SendMessageAsync(Strings.VcToggleOff);
        }
        else
        {
            await BotHost.VcLogger.SetChannelAsync(guildId, ctx.Channel.Id);
            await ctx.Channel.SendMessageAsync(Strings.VcToggleOn);
        }
    }

    [SlashCommand("vcrank", "VC 滞在時間ランキングを表示します")]
    public async Task VcRank(
        InteractionContext ctx,
        [Option("period", "集計期間: day, week, month, all")] string period = "day")
    {
        // Check repo
        var repo = VcLogRepository.TryCreateFromEnv();
        if (repo == null)
        {
            await ctx.Channel.SendMessageAsync("VC セッション集計は未設定です（Postgres 未接続）。");
            return;
        }

        DateTime? since = null;
        string periodLabel = "全期間";
        var now = DateTime.UtcNow;
        switch ((period ?? "day").ToLowerInvariant())
        {
            case "day":
                since = now.AddDays(-1);
                periodLabel = "過去 1 日";
                break;
            case "week":
                since = now.AddDays(-7);
                periodLabel = "過去 1 週間";
                break;
            case "month":
                since = now.AddMonths(-1);
                periodLabel = "過去 1 か月";
                break;
            case "all":
                since = null;
                periodLabel = "全期間";
                break;
            default:
                await ctx.Channel.SendMessageAsync("period は day/week/month/all のいずれかを指定してください。");
                return;
        }

        var guildId = ctx.Guild?.Id ?? 0UL;
        if (guildId == 0)
        {
            await ctx.Channel.SendMessageAsync("サーバー内で実行してください。");
            return;
        }

        var ranks = await repo.GetRankingAsync(guildId, since);

        var embed = new DiscordEmbedBuilder()
            .WithTitle($"VC 滞在時間ランキング ({periodLabel})")
            .WithColor(DiscordColor.Blurple)
            .WithTimestamp(DateTime.UtcNow);

        if (ranks == null || ranks.Count == 0)
        {
            embed.WithDescription("順位データがありません。");
            await ctx.Channel.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(embed));
            return;
        }

        // Build formatted ranking similar to the example
        var sb = new StringBuilder();
        sb.AppendLine("🏆 VCランキング");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"{periodLabel}のVC滞在時間ランキングです！");
        sb.AppendLine();

        int displayCount = Math.Min(ranks.Count, 10);
        int idx = 1;
        int userRankIndex = -1;
        long userTotal = 0;

        for (int i = 0; i < displayCount; i++)
        {
            var (userId, total) = ranks[i];
            string name;
            try
            {
                var member = await ctx.Guild.GetMemberAsync(userId);
                name = member.DisplayName;
            }
            catch
            {
                name = $"<@{userId}>";
            }

            var hours = total / 3600;
            var mins = (total % 3600) / 60;
            var timestr = $"{hours}時間{mins:D2}分";

            string line;
            if (idx == 1) line = $"🥇 {name}　{timestr}";
            else if (idx == 2) line = $"🥈 {name}　{timestr}";
            else if (idx == 3) line = $"🥉 {name}　{timestr}";
            else line = $"{idx,2}. {name}　{timestr}";

            sb.AppendLine(line);
            idx++;
        }

        sb.AppendLine();
        sb.AppendLine("━━━━━━━━━━━━━━━━━━");

        // Find user's rank
        for (int i = 0; i < ranks.Count; i++)
        {
            if (ranks[i].userId == ctx.User.Id)
            {
                userRankIndex = i + 1;
                userTotal = ranks[i].totalSeconds;
                break;
            }
        }

        if (userRankIndex > 0)
        {
            var uh = userTotal / 3600;
            var um = (userTotal % 3600) / 60;
            sb.AppendLine($"👤 あなた");
            sb.AppendLine($"{userRankIndex}位　{uh}時間{um:D2}分");
        }

        embed.WithDescription(sb.ToString());
        await ctx.Channel.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(embed));


    }
    }
