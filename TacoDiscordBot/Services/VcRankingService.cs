using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using TacoDiscordBot.Repository;
using TacoDiscordBot.Services.Interface;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Services;

public sealed class VcRankingService : IVcRankingService
{
    // ランキング永続化:
    // 開いているセッションを管理し、リポジトリ経由で永続化します。
    private readonly VcRankingRepository _repo;

    private readonly ConcurrentDictionary<
        string,
        (long dbId, DateTime joinedAtUtc, ulong channelId)
    > _openSessions = new();

    public VcRankingService()
    {
        _repo = CreateRankingFromEnvOrNull();
    }

    /// <summary>
    /// 音声状態更新を受けてランキング用の永続化を行います。
    /// メッセージ送信は行いません。
    /// </summary>
    public async Task HandleVoiceStateUpdated(DiscordClient client, VoiceStateUpdateEventArgs e)
    {
        // 入退室・チャンネル移動をランキング用のセッションへ変換します。
        try
        {
            if (e.Guild == null)
                return;

            if (_repo == null)
                return; // DB が無ければ処理なし

            var before = e.Before?.Channel;
            var after = e.After?.Channel;

            if (before == null && after != null)
            {
                // 入室
                try
                {
                    var joinedAtUtc = DateTime.UtcNow;

                    Logger.Info(
                        "VcRankingService: record join guild={GuildId} user={UserId} channel={ChannelId}",
                        e.Guild.Id,
                        e.User.Id,
                        after.Id
                    );

                    var id = await _repo.CreateVcSessionAsync(
                        e.Guild.Id,
                        e.User.Id,
                        after.Id,
                        joinedAtUtc
                    );

                    var key = $"{e.Guild.Id}:{e.User.Id}";

                    _openSessions[key] = (id, joinedAtUtc, after.Id);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "VcRankingService: failed to persist join");
                }
            }
            else if (before != null && after == null)
            {
                // 退室
                try
                {
                    var key = $"{e.Guild.Id}:{e.User.Id}";

                    if (_openSessions.TryRemove(key, out var v))
                    {
                        var nowUtc = DateTime.UtcNow;

                        var dur = (long)(nowUtc - v.joinedAtUtc).TotalSeconds;

                        Logger.Info(
                            "VcRankingService: record leave id={SessionId} duration={DurationSeconds}",
                            v.dbId,
                            dur
                        );

                        await _repo.CloseVcSessionAsync(v.dbId, nowUtc, dur);
                    }
                    else
                    {
                        Logger.Info(
                            "VcRankingService: closing latest session fallback guild={GuildId} user={UserId}",
                            e.Guild.Id,
                            e.User.Id
                        );

                        await _repo.CloseLatestSessionForUserAsync(
                            e.Guild.Id,
                            e.User.Id,
                            DateTime.UtcNow
                        );
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "VcRankingService: failed to persist leave");
                }
            }
            else if (before != null && after != null && before.Id != after.Id)
            {
                // チャンネル移動:
                // 前のセッションを閉じて新しいセッションを作成します。
                try
                {
                    var key = $"{e.Guild.Id}:{e.User.Id}";

                    if (_openSessions.TryRemove(key, out var v))
                    {
                        var nowUtc = DateTime.UtcNow;

                        var dur = (long)(nowUtc - v.joinedAtUtc).TotalSeconds;

                        await _repo.CloseVcSessionAsync(v.dbId, nowUtc, dur);
                    }
                    else
                    {
                        await _repo.CloseLatestSessionForUserAsync(
                            e.Guild.Id,
                            e.User.Id,
                            DateTime.UtcNow
                        );
                    }

                    var joinedAtUtc = DateTime.UtcNow;

                    var id = await _repo.CreateVcSessionAsync(
                        e.Guild.Id,
                        e.User.Id,
                        after.Id,
                        joinedAtUtc
                    );

                    _openSessions[key] = (id, joinedAtUtc, after.Id);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "VcRankingService: failed to persist move");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "VcRankingService: unexpected error in HandleVoiceStateUpdated");
        }
    }

    /// <summary>
    /// VC 滞在時間ランキングの Embed を作成します。
    /// </summary>
    public async Task<DiscordEmbedBuilder> BuildRankingEmbedAsync(
        ulong guildId,
        string period,
        DiscordGuild guild,
        DiscordUser requestingUser
    )
    {
        var repo = CreateRankingFromEnvOrNull();

        var embed = new DiscordEmbedBuilder()
            .WithColor(DiscordColor.Blurple)
            .WithTimestamp(DateTime.UtcNow);

        if (repo == null)
        {
            embed.WithTitle("VCランキング").WithDescription(Strings.VcRankingDbNotSet);

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
                embed.WithTitle("VCランキング").WithDescription(Strings.VcRankingInvalidPeriod);

                return embed;
        }

        var ranks = await repo.GetRankingAsync(guildId, since);

        embed.WithTitle($"📣VC 滞在時間ランキング ({periodLabel})");

        if (ranks == null || ranks.Count == 0)
        {
            embed.WithDescription(Strings.VcRankingNoData);

            return embed;
        }

        // ランキングのフォーマットを組み立てる
        var sb = new StringBuilder();

        sb.AppendLine(Strings.VcRankingHeader);

        sb.AppendLine(Strings.VcRankingSeparator);

        sb.AppendLine($"{periodLabel}のVC滞在時間ランキングです！");

        sb.AppendLine();

        var displayCount = Math.Min(ranks.Count, 10);

        var idx = 1;

        for (var i = 0; i < displayCount; i++)
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

            if (idx == 1)
            {
                line = $"🥇 {name}　{timestr}";
            }
            else if (idx == 2)
            {
                line = $"🥈 {name}　{timestr}";
            }
            else if (idx == 3)
            {
                line = $"🥉 {name}　{timestr}";
            }
            else
            {
                line = $"{idx, 2}. {name}　{timestr}";
            }

            sb.AppendLine(line);
            idx++;
        }

        sb.AppendLine();

        sb.AppendLine(Strings.VcRankingSeparator);

        // ユーザーの順位を見つける
        var userRankIndex = -1;
        long userTotal = 0;

        for (var i = 0; i < ranks.Count; i++)
        {
            if (ranks[i].userId != requestingUser.Id)
                continue;

            userRankIndex = i + 1;

            userTotal = ranks[i].totalSeconds;

            break;
        }

        if (userRankIndex > 0)
        {
            var uh = userTotal / 3600;

            var um = (userTotal % 3600) / 60;

            sb.AppendLine("👤 あなた");

            sb.AppendLine($"{userRankIndex}位　{uh}時間{um:D2}分");
        }

        embed.WithDescription(sb.ToString());

        return embed;
    }

    /// <summary>
    /// 環境変数から VcRankingRepository を作成します。
    /// DB 設定が存在しない場合は null を返します。
    /// </summary>
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

            var parts = new List<string> { $"Host={host}", $"Port={port}", $"Database={db}" };

            if (!string.IsNullOrWhiteSpace(user))
                parts.Add($"Username={user}");

            if (!string.IsNullOrWhiteSpace(pass))
                parts.Add($"Password={pass}");

            if (!string.IsNullOrWhiteSpace(ssl))
                parts.Add($"SslMode={ssl}");

            var conn = string.Join(";", parts);

            var baseRepo = new Repository.BaseRepository(conn);

            if (!baseRepo.IsProviderAvailable())
                return null;

            return new VcRankingRepository(baseRepo);
        }
        catch
        {
            return null;
        }
    }
}
