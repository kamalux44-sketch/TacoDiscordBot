using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using TacoDiscordBot.Repository;

namespace TacoDiscordBot.Services;

public class VcRankingService
{
    public VcRankingService()
    {
    }

    public async Task<DiscordEmbedBuilder> BuildRankingEmbedAsync(ulong guildId, string period, DiscordGuild guild, DiscordUser requestingUser)
    {
        var repo = VcRankingRepository.TryCreateFromEnv();
        var embed = new DiscordEmbedBuilder()
            .WithColor(DiscordColor.Blurple)
            .WithTimestamp(DateTime.UtcNow);

        if (repo == null)
        {
            embed.WithTitle("VCランキング")
                .WithDescription("VC セッション集計は未設定です（Postgres 未接続）。");
            return embed;
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
                embed.WithTitle("VCランキング")
                    .WithDescription("period は day/week/month/all のいずれかを指定してください。");
                return embed;
        }

        var ranks = await repo.GetRankingAsync(guildId, since);

        embed.WithTitle($"VC 滞在時間ランキング ({periodLabel})");

        if (ranks == null || ranks.Count == 0)
        {
            embed.WithDescription("順位データがありません。注意: サーバー内でVCログが有効になっているか確認してください。");
            return embed;
        }

        // Build formatted ranking
        var sb = new StringBuilder();
        sb.AppendLine("?? VCランキング");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"{periodLabel}のVC滞在時間ランキングです！");
        sb.AppendLine();

        int displayCount = Math.Min(ranks.Count, 10);
        int idx = 1;

        for (int i = 0; i < displayCount; i++)
        {
            var (userId, total) = ranks[i];
            string name;
            try
            {
                var member = await guild.GetMemberAsync(userId);
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
            if (idx == 1) line = $"?? {name}　{timestr}";
            else if (idx == 2) line = $"?? {name}　{timestr}";
            else if (idx == 3) line = $"?? {name}　{timestr}";
            else line = $"{idx,2}. {name}　{timestr}";

            sb.AppendLine(line);
            idx++;
        }

        sb.AppendLine();
        sb.AppendLine("━━━━━━━━━━━━━━━━━━");

        // Find user's rank
        int userRankIndex = -1;
        long userTotal = 0;
        for (int i = 0; i < ranks.Count; i++)
        {
            if (ranks[i].userId == requestingUser.Id)
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
            sb.AppendLine($"?? あなた");
            sb.AppendLine($"{userRankIndex}位　{uh}時間{um:D2}分");
        }

        embed.WithDescription(sb.ToString());
        return embed;
    }
}
