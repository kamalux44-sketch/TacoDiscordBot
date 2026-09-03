using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Models;
using TacoDiscordBot.Repository;
using TacoDiscordBot.Services.Interface;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Services;

public class BoService : IBoManager, IDeadlineOwner
{
    // BO（募集）管理サービス。
    // メモリ上でセッションを管理し、永続化はオプションで BoRepository を通じて行います。
    private readonly DiscordClient _client;
    private readonly ConcurrentDictionary<string, BoSession> _sessions = new();
    private readonly DeadlineService _deadlineService;
    private readonly BoRepository _repo;
    private DateTime _lastDeadlineCheck;

    private class DeadlineSelection
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int Day { get; set; }
        public int Hour { get; set; }
        public int Minute { get; set; }
    }

    /// <summary>
    /// 指定ユーザーの直近の募集に締め切りを設定します。
    /// 成功した場合 true を返します。
    /// </summary>
    public async Task<bool> ApplyDeadlineToLatestSessionAsync(
        ulong userId,
        DateTime utcDeadline,
        string raw
    )
    {
        var session = _sessions
            .Values.Where(x => x.OwnerId == userId && !x.IsClosed)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();

        if (session == null)
            return false;

        session.Deadline = utcDeadline;
        session.DeadlineRaw = raw;

        await UpdateSessionMessageAsync(session);

        if (_repo != null)
            await _repo.UpdateSessionAsync(session);

        return true;
    }

    private static BoRepository CreateFromEnvOrNull()
    {
        var host = Environment.GetEnvironmentVariable(Strings.EnvPgHost);

        if (string.IsNullOrWhiteSpace(host))
            return null;

        var port = Environment.GetEnvironmentVariable(Strings.EnvPgPort) ?? Strings.DefaultDBPPort;

        var db = Environment.GetEnvironmentVariable(Strings.EnvPgDatabase) ?? Strings.DefaultDBName;

        var user = Environment.GetEnvironmentVariable(Strings.EnvPgUser);

        var pass = Environment.GetEnvironmentVariable(Strings.EnvPgPassword);

        var ssl = Environment.GetEnvironmentVariable(Strings.EnvPgSslMode);

        var parts = new List<string> { $"Host={host}", $"Port={port}", $"Database={db}" };

        if (!string.IsNullOrWhiteSpace(user))
            parts.Add($"Username={user}");

        if (!string.IsNullOrWhiteSpace(pass))
            parts.Add($"Password={pass}");

        if (!string.IsNullOrWhiteSpace(ssl))
            parts.Add($"SslMode={ssl}");

        var conn = string.Join(";", parts);

        var baseRepo = new BaseRepository(conn);

        if (!baseRepo.IsProviderAvailable())
            return null;

        return new BoRepository(baseRepo);
    }

    public BoService(DiscordClient client, BoRepository repo = null)
    {
        _client = client;

        Logger.Info("BoManager: 初期化開始");

        _repo = repo ?? CreateFromEnvOrNull();

        if (_repo != null)
        {
            var persisted = _repo.LoadActiveSessionsAsync().GetAwaiter().GetResult();

            foreach (var session in persisted)
            {
                _sessions[session.SessionId] = session;
            }

            Logger.Info(
                "BoManager: DB からセッションを読み込み 件数={SessionCount}",
                persisted.Count
            );
        }

        // 作成から7日を超えた古い募集を1時間ごとにクリーンアップします。
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await CleanExpiredSessionsAsync();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "期限切れセッションクリーンアップループ");
                }

                await Task.Delay(TimeSpan.FromHours(1));
            }
        });

        Logger.Info("BoManager: 期限切れクリーンアップタスクを開始");

        _deadlineService = new DeadlineService(this);

        // 1分ごとに締め切りを確認します。
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    var prev = _lastDeadlineCheck;

                    foreach (var session in _sessions.Values)
                    {
                        if (session == null)
                            continue;

                        if (session.IsClosed)
                            continue;

                        if (!session.Deadline.HasValue)
                            continue;

                        if (session.Deadline.Value <= prev || session.Deadline.Value > now)
                        {
                            continue;
                        }

                        // 二重通知防止
                        session.IsClosed = true;

                        var mentions =
                            session.Participants != null && session.Participants.Count > 0
                                ? string.Join(" ", session.Participants.Select(id => $"<@{id}>"))
                                : string.Empty;

                        var header = !string.IsNullOrWhiteSpace(session.Body)
                            ? $"📢 『{session.Body}』 の募集は締め切りです！"
                            : "📢 募集は締め切りです！";

                        var channel = await _client.GetChannelAsync(session.ChannelId);

                        if (channel != null)
                        {
                            await channel.SendMessageAsync(
                                string.IsNullOrWhiteSpace(mentions)
                                    ? header
                                    : header + " " + mentions
                            );

                            var message = await channel.GetMessageAsync(session.MessageId);

                            if (message != null)
                            {
                                await message.ModifyAsync(m =>
                                {
                                    m.Content =
                                        (message.Content ?? string.Empty)
                                        + "\n\n**（締め切り済み）**";
                                });
                            }
                        }

                        if (_repo != null)
                        {
                            await _repo.UpdateSessionAsync(session);
                        }
                    }

                    _lastDeadlineCheck = now;
                }
                catch (Exception ex)
                {
                    // 1回の失敗で定期チェック自体が終了しないようにする
                    Logger.Error(ex, "締め切り確認ループ");
                }

                await Task.Delay(TimeSpan.FromMinutes(1));
            }
        });
    }

    public async Task HandleComponentInteraction(
        DiscordClient client,
        ComponentInteractionCreateEventArgs e
    )
    {
        var id = e.Id ?? e.Interaction?.Data?.CustomId;

        // コンポーネント種別を判定し、担当サービスへ処理を委譲します。
        Logger.Info(
            "Component interaction received: e.Id={InteractionId} CustomId={CustomId} UserId={UserId}",
            id,
            e.Interaction?.Data?.CustomId,
            e.User?.Id
        );

        if (string.IsNullOrEmpty(id))
            return;

        if (await HandleDeadlineInteractionAsync(id, e))
            return;

        if (!TryParseBoInteraction(id, out var action, out var sessionId))
            return;

        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            await CreateResponseAsync(e, "募集が見つかりませんでした。");

            return;
        }

        await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);

        if (action == "bo_close")
        {
            await HandleCloseActionAsync(e, session);

            return;
        }

        if (session.IsClosed)
        {
            await NotifyClosedSessionAsync(e);
            return;
        }

        await HandleJoinOrCancelAsync(e, action, session);
    }

    private async Task<bool> HandleDeadlineInteractionAsync(
        string id,
        ComponentInteractionCreateEventArgs e
    )
    {
        if (!id.StartsWith("deadline_"))
            return false;

        // 締め切りコンポーネントは DeadlineService に委譲します。
        return await _deadlineService.HandleInteractionAsync(e);
    }

    private static bool TryParseBoInteraction(
        string id,
        out string action,
        out string sessionId
    )
    {
        action = null;
        sessionId = null;

        if (!id.StartsWith("bo_join:")
            && !id.StartsWith("bo_cancel:")
            && !id.StartsWith("bo_close:"))
            return false;

        var parts = id.Split(':', 2);

        if (parts.Length != 2)
            return false;

        action = parts[0];
        sessionId = parts[1];
        return true;
    }

    private static async Task NotifyClosedSessionAsync(ComponentInteractionCreateEventArgs e)
    {
        await e.Interaction.CreateFollowupMessageAsync(
            new DiscordFollowupMessageBuilder()
                .WithContent("この募集はすでに終了しています。")
                .AsEphemeral(true)
        );
    }

    private async Task HandleCloseActionAsync(
        ComponentInteractionCreateEventArgs e,
        BoSession session
    )
    {
        if (e.User.Id != session.OwnerId)
        {
            await e.Interaction.CreateFollowupMessageAsync(
                new DiscordFollowupMessageBuilder()
                    .WithContent("募集主のみ募集終了できます。")
                    .AsEphemeral(true)
            );

            return;
        }

        if (session.IsClosed)
            return;

        session.IsClosed = true;

        var channel = await _client.GetChannelAsync(session.ChannelId);

        if (channel != null)
        {
            if (!string.IsNullOrWhiteSpace(session.Body))
            {
                await channel.SendMessageAsync($"📢 募集を終了しました：『{session.Body}』");
            }
            else
            {
                await channel.SendMessageAsync("📢 募集を終了しました。");
            }

            var message = await channel.GetMessageAsync(session.MessageId);

            if (message != null)
            {
                await message.ModifyAsync(m =>
                {
                    m.Content = (message.Content ?? string.Empty) + "\n\n**（募集終了）**";
                });
            }
        }

        if (_repo != null)
        {
            await _repo.UpdateSessionAsync(session);
        }
    }

    private async Task HandleJoinOrCancelAsync(
        ComponentInteractionCreateEventArgs e,
        string action,
        BoSession session
    )
    {
        // 参加者を更新し、定員に達した場合は募集を自動終了します。
        var previousCount = session.Participants.Count;

        lock (session)
        {
            if (action == "bo_join")
            {
                if (!session.Participants.Contains(e.User.Id))
                {
                    var capacity = session.At > 0 ? session.At + 1 : int.MaxValue;

                    if (session.Participants.Count < capacity)
                    {
                        session.Participants.Add(e.User.Id);
                    }
                }
            }
            else if (action == "bo_cancel")
            {
                session.Participants.RemoveAll(x => x == e.User.Id);
            }
        }

        await UpdateSessionMessageAsync(session);

        var currentCount = session.Participants.Count;

        var capacityCheck = session.At > 0 ? session.At + 1 : int.MaxValue;

        if (session.At <= 0 || previousCount >= capacityCheck || currentCount < capacityCheck)
        {
            return;
        }

        session.IsClosed = true;

        var channel = await _client.GetChannelAsync(session.ChannelId);

        if (channel != null)
        {
            var mentions = string.Join(" ", session.Participants.Select(userId => $"<@{userId}>"));

            if (!string.IsNullOrWhiteSpace(session.Body))
            {
                await channel.SendMessageAsync(
                    $"📢 人数が集まりました！（募集: 『{session.Body}』） {mentions}"
                );
            }
            else
            {
                await channel.SendMessageAsync($"📢 人数が集まりました！ {mentions}");
            }

            var message = await channel.GetMessageAsync(session.MessageId);

            if (message != null)
            {
                await message.ModifyAsync(m =>
                {
                    m.Content = (message.Content ?? string.Empty) + "\n\n**（募集終了）**";
                });
            }
        }

        if (_repo != null)
        {
            await _repo.UpdateSessionAsync(session);
        }
    }

    public async Task CreateSessionAsync(
        InteractionContext ctx,
        string body,
        int at,
        string rank,
        DateTime? deadline = null,
        string description = ""
    )
    {
        var sessionId = Guid.NewGuid().ToString();

        var session = new BoSession
        {
            SessionId = sessionId,
            MessageId = 0,
            ChannelId = ctx.Channel.Id,
            Body = body,
            At = at,
            Rank = rank ?? string.Empty,

            DeadlineRaw = deadline.HasValue
                ? deadline.Value.ToString(Strings.DateTimeFormat)
                : string.Empty,

            Description = description ?? string.Empty,

            OwnerId = ctx.User.Id,

            Participants = new List<ulong> { ctx.User.Id },

            CreatedAt = DateTime.UtcNow,

            IsClosed = false,
        };

        // 入力された締め切りは日本時間として扱い、UTCに変換します。
        if (deadline.HasValue)
        {
            var jst = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");

            var unspecifiedDeadline = DateTime.SpecifyKind(
                deadline.Value,
                DateTimeKind.Unspecified
            );

            session.Deadline = TimeZoneInfo.ConvertTimeToUtc(unspecifiedDeadline, jst);
        }

        var participantsText = string.Join(
            "\n",
            session.Participants.Select((id, idx) => $"{idx + 1}. <@{id}>")
        );

        if (string.IsNullOrEmpty(participantsText))
        {
            participantsText = "—";
        }

        var atText =
            session.At > 0
                ? $"{session.Participants.Count}/{session.At + 1}"
                : $"{session.Participants.Count}/任意";

        var embedBuilder = new DiscordEmbedBuilder()
            .WithTitle(Strings.EmbedTitle)
            .WithColor(DiscordColor.Blurple)
            .WithTimestamp(DateTime.UtcNow);

        if (!string.IsNullOrWhiteSpace(session.Body))
        {
            embedBuilder.AddField("🎮 " + Strings.LabelContent, session.Body, false);
        }

        embedBuilder.AddField("👤 " + Strings.LabelOwner, $"<@{session.OwnerId}>", false);

        if (!string.IsNullOrWhiteSpace(session.Rank))
        {
            embedBuilder.AddField("🏅 " + Strings.LabelRank, session.Rank, false);
        }

        if (session.Deadline.HasValue)
        {
            embedBuilder.AddField(
                Strings.LabelDeadline,
                TimeZoneInfo
                    .ConvertTimeFromUtc(
                        DateTime.SpecifyKind(session.Deadline.Value, DateTimeKind.Utc),
                        TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time")
                    )
                    .ToString(Strings.DateTimeFormat),
                false
            );
        }
        else if (!string.IsNullOrWhiteSpace(session.DeadlineRaw))
        {
            embedBuilder.AddField(Strings.LabelDeadline, session.DeadlineRaw, false);
        }

        if (!string.IsNullOrWhiteSpace(session.Description))
        {
            embedBuilder.AddField(Strings.LabelDescription, session.Description, false);
        }

        embedBuilder.AddField(
            Strings.ParticipantsFieldPrefix + Strings.LabelParticipants,
            participantsText,
            false
        );

        if (session.At > 0)
        {
            embedBuilder.WithFooter(Strings.FooterParticipantCount + atText);
        }

        var isMinimal =
            string.IsNullOrWhiteSpace(session.Body)
            && session.At == 0
            && string.IsNullOrWhiteSpace(session.Rank)
            && string.IsNullOrWhiteSpace(session.DeadlineRaw)
            && string.IsNullOrWhiteSpace(session.Description);

        string content;

        if (isMinimal)
        {
            content = string.Format(Strings.ContentMinimalTemplate, session.OwnerId);
        }
        else if (!string.IsNullOrWhiteSpace(session.Body))
        {
            content = string.Format(Strings.ContentWithBodyTemplate, session.OwnerId, session.Body);
        }
        else
        {
            content = Strings.EmbedStartContent;
        }

        var builder = new DiscordMessageBuilder()
            .WithContent(content)
            .AddComponents(
                new DiscordComponent[]
                {
                    new DiscordButtonComponent(
                        ButtonStyle.Primary,
                        $"bo_join:{sessionId}",
                        Strings.ButtonJoinLabel
                    ),
                    new DiscordButtonComponent(
                        ButtonStyle.Secondary,
                        $"bo_cancel:{sessionId}",
                        Strings.ButtonCancelParticipationLabel
                    ),
                    new DiscordButtonComponent(
                        ButtonStyle.Danger,
                        $"bo_close:{sessionId}",
                        Strings.ButtonCloseLabel
                    ),
                }
            )
            .AddEmbed(embedBuilder);

        var message = await ctx.Channel.SendMessageAsync(builder);

        session.MessageId = message.Id;

        _sessions[sessionId] = session;

        if (_repo != null)
        {
            await _repo.CreateSessionAsync(session);
        }
    }

    /// <summary>
    /// 作成から7日を超えた、締め切り未設定の募集を終了します。
    /// メッセージも削除します。
    /// </summary>
    private async Task CleanExpiredSessionsAsync()
    {
        var expiry = TimeSpan.FromDays(7);

        var now = DateTime.UtcNow;

        var toClose = new List<BoSession>();

        foreach (var session in _sessions.Values)
        {
            if (!session.IsClosed && !session.Deadline.HasValue && now - session.CreatedAt > expiry)
            {
                toClose.Add(session);
            }
        }

        foreach (var session in toClose)
        {
            session.IsClosed = true;

            var channel = await _client.GetChannelAsync(session.ChannelId);

            if (channel != null)
            {
                var message = await channel.GetMessageAsync(session.MessageId);

                if (message != null)
                {
                    await message.DeleteAsync();
                }
            }

            if (_repo != null)
            {
                await _repo.CloseSessionAsync(session.SessionId);
            }
        }
    }

    private async Task UpdateSessionMessageAsync(BoSession session)
    {
        var participantsText =
            session.Participants.Count == 0
                ? "—"
                : string.Join(
                    "\n",
                    session.Participants.Select((id, idx) => $"{idx + 1}. <@{id}>")
                );

        var currentCount = session.Participants.Count;

        var atText = session.At > 0 ? $"{currentCount}/{session.At + 1}" : $"{currentCount}/任意";

        var embedBuilder = new DiscordEmbedBuilder()
            .WithTitle(Strings.EmbedTitle)
            .WithColor(DiscordColor.Blurple)
            .WithTimestamp(DateTime.UtcNow);

        if (!string.IsNullOrWhiteSpace(session.Body))
        {
            embedBuilder.AddField("🎮 " + Strings.LabelContent, session.Body, false);
        }

        embedBuilder.AddField("👤 " + Strings.LabelOwner, $"<@{session.OwnerId}>", false);

        if (!string.IsNullOrWhiteSpace(session.Rank))
        {
            embedBuilder.AddField("🏅 " + Strings.LabelRank, session.Rank, false);
        }

        if (session.Deadline.HasValue)
        {
            embedBuilder.AddField(
                "⏰ 締切",
                TimeZoneInfo
                    .ConvertTimeFromUtc(
                        DateTime.SpecifyKind(session.Deadline.Value, DateTimeKind.Utc),
                        TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time")
                    )
                    .ToString(Strings.DateTimeFormat),
                false
            );
        }
        else if (!string.IsNullOrWhiteSpace(session.DeadlineRaw))
        {
            embedBuilder.AddField("⏰ 締切", session.DeadlineRaw, false);
        }

        if (!string.IsNullOrWhiteSpace(session.Description))
        {
            embedBuilder.AddField("📝 説明", session.Description, false);
        }

        embedBuilder.AddField("📋 " + Strings.LabelParticipants, participantsText, false);

        if (session.At > 0)
        {
            embedBuilder.WithFooter("参加数: " + atText);
        }

        var isMinimal =
            string.IsNullOrWhiteSpace(session.Body)
            && session.At == 0
            && string.IsNullOrWhiteSpace(session.Rank)
            && string.IsNullOrWhiteSpace(session.DeadlineRaw)
            && string.IsNullOrWhiteSpace(session.Description);

        string content;

        if (isMinimal)
        {
            content = $"@here\n<@{session.OwnerId}>さんが何か募集しているようです";
        }
        else if (!string.IsNullOrWhiteSpace(session.Body))
        {
            content = $"@here\n<@{session.OwnerId}>さんが{session.Body}の募集を更新しました！";
        }
        else
        {
            content = "@here\n募集を更新しました！";
        }

        var builder = new DiscordMessageBuilder()
            .WithContent(content)
            .AddComponents(
                new DiscordComponent[]
                {
                    new DiscordButtonComponent(
                        ButtonStyle.Primary,
                        $"bo_join:{session.SessionId}",
                        "参加"
                    ),
                    new DiscordButtonComponent(
                        ButtonStyle.Secondary,
                        $"bo_cancel:{session.SessionId}",
                        "参加取消"
                    ),
                    new DiscordButtonComponent(
                        ButtonStyle.Danger,
                        $"bo_close:{session.SessionId}",
                        "募集終了"
                    ),
                }
            )
            .AddEmbed(embedBuilder);

        var channel = await _client.GetChannelAsync(session.ChannelId);

        if (channel == null)
            return;

        var message = await channel.GetMessageAsync(session.MessageId);

        if (message == null)
            return;

        Logger.Info(
            $"Updating message: "
                + $"ChannelId={session.ChannelId} "
                + $"MessageId={session.MessageId} "
                + $"ContentLen={(builder.Content?.Length ?? 0)} "
                + $"EmbedTitle={(embedBuilder.Title ?? "(null)")} "
                + $"Fields={embedBuilder.Fields.Count}"
        );

        await message.ModifyAsync(builder);
    }

    private async Task CreateResponseAsync(ComponentInteractionCreateEventArgs e, string content)
    {
        await e.Interaction.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(content).AsEphemeral(true)
        );
    }
}
