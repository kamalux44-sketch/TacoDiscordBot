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
        var repo = CreateRankingFromEnvOrNull();
        var embed = new DiscordEmbedBuilder()
            .WithColor(DiscordColor.Blurple)
            .WithTimestamp(DateTime.UtcNow);

        if (repo == null)
        {
            embed.WithTitle("VCランキング")
                .WithDescription(Strings.VcRankingDbNotSet);
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
                    .WithDescription(Strings.VcRankingInvalidPeriod);
                return embed;
        }

        var ranks = await repo.GetRankingAsync(guildId, since);

        embed.WithTitle($"📣VC 滞在時間ランキング ({periodLabel})");

        if (ranks == null || ranks.Count == 0)
        {
            embed.WithDescription(Strings.VcRankingNoData);
            return embed;
        }

        // Build formatted ranking
        var sb = new StringBuilder();
        sb.AppendLine(Strings.VcRankingHeader);
        sb.AppendLine(Strings.VcRankingSeparator);
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
            if (idx == 1) line = $"🥇 {name}　{timestr}";
            else if (idx == 2) line = $"🥈 {name}　{timestr}";
            else if (idx == 3) line = $"🥉 {name}　{timestr}";
            else line = $"{idx,2}. {name}　{timestr}";

            sb.AppendLine(line);
            idx++;
        }

        sb.AppendLine();
        sb.AppendLine(Strings.VcRankingSeparator);

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
            sb.AppendLine($"👤 あなた");
            sb.AppendLine($"{userRankIndex}位　{uh}時間{um:D2}分");
        }

        embed.WithDescription(sb.ToString());
        return embed;
    }

    private static VcRankingRepository CreateRankingFromEnvOrNull()
    {
        try
        {
            var host = Environment.GetEnvironmentVariable("PGHOST");
            if (string.IsNullOrWhiteSpace(host))
                return null;

            var port = Environment.GetEnvironmentVariable("PGPORT") ?? Strings.DefaultDBPPort;
            var db = Environment.GetEnvironmentVariable("PGDATABASE") ?? Strings.DefaultDBName;
            var user = Environment.GetEnvironmentVariable("PGUSER");
            var pass = Environment.GetEnvironmentVariable("PGPASSWORD");
            var ssl = Environment.GetEnvironmentVariable("PGSSLMODE");

            var parts = new System.Collections.Generic.List<string>
            {
                $"Host={host}",
                $"Port={port}",
                $"Database={db}"
            };
            if (!string.IsNullOrWhiteSpace(user)) parts.Add($"Username={user}");
            if (!string.IsNullOrWhiteSpace(pass)) parts.Add($"Password={pass}");
            if (!string.IsNullOrWhiteSpace(ssl)) parts.Add($"SslMode={ssl}");

            var conn = string.Join(";", parts);
            var baseRepo = new Repository.BaseRepository(conn, s => Console.WriteLine($"[DB] {s}"));
            if (!baseRepo.IsProviderAvailable()) return null;
            return new VcRankingRepository(baseRepo);
        }
        catch
        {
            return null;
        }
    }
}

