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
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Services;

public class BoManager
{
    private readonly DiscordClient _client;

    private readonly ConcurrentDictionary<string, Models.BoSession> _sessions = new();

    // BOセッションはメモリ管理とします。
    // 永続化は行いません。
    public BoManager(DiscordClient client)
    {
        _client = client;

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

        // 締め切りチェック用の定期タスク
        // 1分ごとに締め切りを確認します。
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var now = DateTime.UtcNow;

                    foreach (var kv in _sessions)
                    {
                        var session = kv.Value;

                        if (session == null)
                            continue;

                        // すでに終了している募集は締切通知を送らない
                        if (session.IsClosed)
                            continue;

                        if (session.Deadline.HasValue &&
                            now >= session.Deadline.Value)
                        {
                            // 二重通知防止
                            session.IsClosed = true;
                            // 通知とメッセージ編集は安全なヘルパーに委譲
                            var mentions =
                                session.Participants != null &&
                                session.Participants.Count > 0
                                    ? string.Join(
                                        " ",
                                        session.Participants.Select(
                                            id => $"<@{id}>"))
                                    : string.Empty;

                            if (string.IsNullOrWhiteSpace(mentions))
                            {
                                _ = SafeSendChannelMessageAsync(session.ChannelId, "締め切りです！");
                            }
                            else
                            {
                                _ = SafeSendChannelMessageAsync(session.ChannelId, $"締め切りです！ {mentions}");
                            }

                            _ = SafeAppendToMessageAsync(session.ChannelId, session.MessageId, "\n\n**（締め切り済み）**");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "締め切り確認ループ");
                }

                await Task.Delay(TimeSpan.FromMinutes(1));
            }
        });
    }

    public async Task HandleComponentInteraction(
        DiscordClient client,
        ComponentInteractionCreateEventArgs e)
    {
        // e.Id が null/空 の場合、Interaction.Data の CustomId を代替として使用する
        var id = e.Id ?? e?.Interaction?.Data?.CustomId;

        // ログを追加して、どのカスタムIDが送信されているかを確認します。
        Logger.Info($"Component interaction received: e.Id={id} CustomId={e?.Interaction?.Data?.CustomId} UserId={e?.User?.Id}");


        if (string.IsNullOrEmpty(id))
            return;

        // 参加
        // 参加取消
        // 募集終了
        if (id.StartsWith("bo_join:") ||
            id.StartsWith("bo_cancel:") ||
            id.StartsWith("bo_close:"))
        {
            var parts = id.Split(':', 2);

            if (parts.Length != 2)
                return;

            var action = parts[0];
            var sessionId = parts[1];

            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                _ = SafeCreateResponseAsync(e, "募集が見つかりませんでした。");
                return;
            }

            // Interaction ACK（失敗しても続行）
            try
            {
                await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            }
            catch
            {
                // ACK 失敗は無視
            }

            try
            {
                if (action == "bo_close")
                {
                    await HandleCloseActionAsync(e, session);
                    return;
                }

                // すでに終了している場合は通知して終わり
                if (session.IsClosed)
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent("この募集はすでに終了しています。").AsEphemeral(true));
                    return;
                }

                await HandleJoinOrCancelAsync(e, action, session);
            }
            catch (Exception ex)
            {
                try
                {
                    await ReportErrorAsync(session.ChannelId, ex);
                }
                catch
                {
                    // 二次エラーは無視
                }

                Logger.Error(ex, "コンポーネント処理");
            }
        }
    }

    private async Task HandleCloseActionAsync(ComponentInteractionCreateEventArgs e, Models.BoSession session)
    {
        // 募集主のみ募集終了可能
        if (e.User.Id != session.OwnerId)
        {
            await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent("募集主のみ募集終了できます。").AsEphemeral(true));
            return;
        }

        if (session.IsClosed)
            return;

        session.IsClosed = true;

        var ch = await _client.GetChannelAsync(session.ChannelId);
        if (ch != null)
        {
            await ch.SendMessageAsync("📢 募集を終了しました。");

            var msg = await ch.GetMessageAsync(session.MessageId);
            if (msg != null)
            {
                await msg.ModifyAsync(m => { m.Content = (msg.Content ?? string.Empty) + "\n\n**（募集終了）**"; });
            }
        }
    }

    private async Task HandleJoinOrCancelAsync(ComponentInteractionCreateEventArgs e, string action, Models.BoSession session)
    {
        // 変更前の参加人数
        var prevCount = session.Participants.Count;

        lock (session)
        {
            if (action == "bo_join")
            {
                if (!session.Participants.Contains(e.User.Id))
                {
                    var capacity = session.At > 0 ? session.At + 1 : int.MaxValue;
                    if (session.Participants.Count < capacity)
                        session.Participants.Add(e.User.Id);
                }
            }
            else if (action == "bo_cancel")
            {
                session.Participants.RemoveAll(x => x == e.User.Id);
            }
        }

        await UpdateSessionMessageAsync(session);

        // 定員到達チェック
        var cur = session.Participants.Count;
        var capacityCheck = session.At > 0 ? session.At + 1 : int.MaxValue;

        if (session.At > 0 && prevCount < capacityCheck && cur >= capacityCheck)
        {
            session.IsClosed = true;

            var ch = await _client.GetChannelAsync(session.ChannelId);
            if (ch != null)
            {
                var mentions = string.Join(" ", session.Participants.Select(userId => $"<@{userId}>"));
                await ch.SendMessageAsync($"人数が集まりました！ {mentions}");

                var msg = await ch.GetMessageAsync(session.MessageId);
                if (msg != null)
                {
                    await msg.ModifyAsync(m => { m.Content = (msg.Content ?? string.Empty) + "\n\n**（募集終了）**"; });
                }
            }
        }
    }

    public async Task CreateSessionAsync(
        InteractionContext ctx,
        string game,
        int at,
        string rank,
        DateTime? deadline = null,
        string description = "")
    {
        try
        {
            var sessionId = Guid.NewGuid().ToString();

            // セッションを作成し、
            // 募集主を参加者の先頭に追加
            var session = new Models.BoSession
            {
                SessionId = sessionId,
                MessageId = 0,
                ChannelId = ctx.Channel.Id,
                Game = game,
                At = at,
                Rank = rank ?? string.Empty,

                DeadlineRaw =
                    deadline.HasValue
                        ? deadline.Value.ToString(
                            Strings.DateTimeFormat)
                        : string.Empty,

                Description = description ?? string.Empty,

                OwnerId = ctx.User.Id,

                Participants =
                    new List<ulong>
                    {
                        ctx.User.Id
                    },

                CreatedAt = DateTime.UtcNow,

                IsClosed = false
            };

            // 締め切りが指定されている場合、
            // 入力値を日本時間（JST）としてUTCに変換して保存
            if (deadline.HasValue)
            {
                var jst =
                    TimeZoneInfo.FindSystemTimeZoneById(
                        "Tokyo Standard Time");

                var unspecifiedDeadline =
                    DateTime.SpecifyKind(
                        deadline.Value,
                        DateTimeKind.Unspecified);

                session.Deadline =
                    TimeZoneInfo.ConvertTimeToUtc(
                        unspecifiedDeadline,
                        jst);
            }

            // ==============================
            // 参加者一覧
            // ==============================
            var participantsText =
                string.Join(
                    "\n",
                    session.Participants.Select(
                        (id, idx) =>
                            $"{idx + 1}. <@{id}>"));

            if (string.IsNullOrEmpty(participantsText))
            {
                participantsText = "—";
            }

            // ==============================
            // 募集人数
            // ==============================
            var atText =
                session.At > 0
                    ? $"{session.Participants.Count}/{session.At + 1}"
                    : $"{session.Participants.Count}/任意";

            // ==============================
            // Embed
            // ==============================
            var embed =
                new DiscordEmbedBuilder()
                    .WithTitle(Strings.EmbedTitle)

                    .AddField(
                        "🎮 " + Strings.LabelGame,
                        string.IsNullOrWhiteSpace(session.Game)
                            ? "未設定"
                            : session.Game,
                        false)

                    .AddField(
                        "👤 " + Strings.LabelOwner,
                        $"<@{session.OwnerId}>",
                        true)

                    .AddField(
                        "🏅 " + Strings.LabelRank,
                        string.IsNullOrEmpty(session.Rank)
                            ? "未設定"
                            : session.Rank,
                        true)

                    .AddField(
                        "⏰ 締切",
                        session.Deadline.HasValue
                            ? TimeZoneInfo.ConvertTimeFromUtc(
                                DateTime.SpecifyKind(
                                    session.Deadline.Value,
                                    DateTimeKind.Utc),
                                TimeZoneInfo.FindSystemTimeZoneById(
                                    "Tokyo Standard Time"))
                                .ToString(
                                    Strings.DateTimeFormat)
                            : (string.IsNullOrWhiteSpace(
                                session.DeadlineRaw)
                                ? "—"
                                : session.DeadlineRaw),
                        true)

                    .AddField(
                        "📝 説明",
                        string.IsNullOrWhiteSpace(
                            session.Description)
                            ? "—"
                            : session.Description,
                        false)

                    .AddField(
                        "📋 " + Strings.LabelParticipants,
                        participantsText,
                        false)

                    .WithFooter(
                        "参加数: " + atText)

                    .WithColor(
                        DiscordColor.Blurple)

                    .WithTimestamp(
                        DateTime.UtcNow);

            // ==============================
            // 募集メッセージ
            // ==============================
            var builder =
                new DiscordMessageBuilder()
                    .WithContent(
                        $"@here\n**{(
                            string.IsNullOrWhiteSpace(
                                session.Game)
                                ? "募集"
                                : session.Game
                        )}** の募集を開始しました！")

                    .AddEmbed(embed)

                    .AddComponents(
                        new DiscordComponent[]
                        {
                            new DiscordButtonComponent(
                                ButtonStyle.Primary,
                                $"bo_join:{sessionId}",
                                "参加"),

                            new DiscordButtonComponent(
                                ButtonStyle.Secondary,
                                $"bo_cancel:{sessionId}",
                                "参加取消"),

                            new DiscordButtonComponent(
                                ButtonStyle.Danger,
                                $"bo_close:{sessionId}",
                                "募集終了")
                        });

            var msg =
                await ctx.Channel.SendMessageAsync(
                    builder);

            // DiscordメッセージIDを確定
            session.MessageId = msg.Id;

            // セッションをメモリに保存
            _sessions[sessionId] = session;
        }
        catch (Exception ex)
        {
            try
            {
                var msg =
                    Strings.ErrorMessages[
                        Random.Shared.Next(
                            Strings.ErrorMessages.Length)];

                await ctx.Channel.SendMessageAsync(msg);
            }
            catch
            {
                // 通知失敗は無視
            }

            Logger.Error(ex, "募集作成");
        }
    }

    /// <summary>
    /// 作成から7日を超えた募集を破棄します。
    /// メッセージも削除します。
    /// </summary>
    private async Task CleanExpiredSessionsAsync()
    {
        var expiry =
            TimeSpan.FromDays(7);

        var now =
            DateTime.UtcNow;

        var toRemove =
            new List<string>();

        foreach (var kv in _sessions)
        {
            var session = kv.Value;

            if (now - session.CreatedAt > expiry)
            {
                toRemove.Add(kv.Key);
            }
        }

        foreach (var id in toRemove)
        {
            if (_sessions.TryRemove(
                    id,
                    out var session))
            {
                try
                {
                    var ch =
                        await _client.GetChannelAsync(
                            session.ChannelId);

                    if (ch != null)
                    {
                        var msg =
                            await ch.GetMessageAsync(
                                session.MessageId);

                        if (msg != null)
                        {
                            await msg.DeleteAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        await ReportErrorAsync(
                            session.ChannelId,
                            ex);
                    }
                    catch
                    {
                        // 通知失敗は無視
                    }

                    Logger.Error(ex, "メインループ");
                }
            }
        }
    }

    private async Task UpdateSessionMessageAsync(
        Models.BoSession session)
    {
        try
        {
            // ==============================
            // 参加者一覧
            // ==============================
            var participantsText =
                session.Participants.Count == 0
                    ? "—"
                    : string.Join(
                        "\n",
                        session.Participants.Select(
                            (id, idx) =>
                                $"{idx + 1}. <@{id}>"));

            var cur =
                session.Participants.Count;

            var atText =
                session.At > 0
                    ? $"{cur}/{session.At + 1}"
                    : $"{cur}/任意";

            // ==============================
            // Embed
            // ==============================
            var embed =
                new DiscordEmbedBuilder()
                    .WithTitle(Strings.EmbedTitle)

                    .AddField(
                        "🎮 " + Strings.LabelGame,
                        string.IsNullOrWhiteSpace(
                            session.Game)
                            ? "未設定"
                            : session.Game,
                        false)

                    .AddField(
                        "👤 " + Strings.LabelOwner,
                        $"<@{session.OwnerId}>",
                        true)

                    .AddField(
                        "🏅 " + Strings.LabelRank,
                        string.IsNullOrEmpty(
                            session.Rank)
                            ? "未設定"
                            : session.Rank,
                        true)

                    .AddField(
                        "⏰ 締切",
                        session.Deadline.HasValue
                            ? TimeZoneInfo.ConvertTimeFromUtc(
                                DateTime.SpecifyKind(
                                    session.Deadline.Value,
                                    DateTimeKind.Utc),
                                TimeZoneInfo.FindSystemTimeZoneById(
                                    "Tokyo Standard Time"))
                                .ToString(
                                    Strings.DateTimeFormat)
                            : (string.IsNullOrWhiteSpace(
                                session.DeadlineRaw)
                                ? "—"
                                : session.DeadlineRaw),
                        true)

                    .AddField(
                        "📝 説明",
                        string.IsNullOrWhiteSpace(
                            session.Description)
                            ? "—"
                            : session.Description,
                        false)

                    .AddField(
                        "📋 " + Strings.LabelParticipants,
                        participantsText,
                        false)

                    .WithFooter(
                        "参加数: " + atText)

                    .WithColor(
                        DiscordColor.Blurple)

                    .WithTimestamp(
                        DateTime.UtcNow);

            // ==============================
            // メッセージ
            // ==============================
            var builder =
                new DiscordMessageBuilder()
                    .WithContent(
                        $"@here\n**{(
                            string.IsNullOrWhiteSpace(
                                session.Game)
                                ? "募集"
                                : session.Game
                        )}** の募集を更新しました！")

                    .AddEmbed(embed)

                    .AddComponents(
                        new DiscordComponent[]
                        {
                            new DiscordButtonComponent(
                                ButtonStyle.Primary,
                                $"bo_join:{session.SessionId}",
                                "参加"),

                            new DiscordButtonComponent(
                                ButtonStyle.Secondary,
                                $"bo_cancel:{session.SessionId}",
                                "参加取消"),

                            new DiscordButtonComponent(
                                ButtonStyle.Danger,
                                $"bo_close:{session.SessionId}",
                                "募集終了")
                        });

            // 元の募集メッセージを取得
            var ch =
                await _client.GetChannelAsync(
                    session.ChannelId);

            if (ch != null)
            {
                var msg =
                    await ch.GetMessageAsync(
                        session.MessageId);

                if (msg != null)
                {
                    try
                    {
                        // デバッグ情報を出力
                        try
                        {
                            Logger.Info($"Updating message: ChannelId={session.ChannelId} MessageId={session.MessageId} ContentLen={(builder.Content?.Length ?? 0)} EmbedTitle={(embed?.Title ?? "(null)")} Fields={embed?.Fields?.Count ?? 0}");
                        }
                        catch { }

                        // DiscordMessageBuilder をそのまま使ってメッセージを更新する
                        await msg.ModifyAsync(builder);
                    }
                    catch (Exception ex)
                    {
                        // 失敗時に詳細をログに出す
                        try
                        {
                            Util.Logger.Error(ex, "メッセージ編集失敗");
                        }
                        catch { }

                        throw;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                await ReportErrorAsync(
                    session.ChannelId,
                    ex);
            }
            catch
            {
                // 通知失敗は無視
            }

            Logger.Error(ex, "募集メッセージ更新");
        }
    }

    /// <summary>
    /// 指定チャンネルへエラー文言を送信し、
    /// コンソールに例外情報を出力します。
    /// </summary>
    private async Task ReportErrorAsync(
        ulong channelId,
        Exception ex)
    {
        try
        {
            var ch =
                await _client.GetChannelAsync(
                    channelId);

            if (ch != null)
            {
                var msg =
                    Strings.ErrorMessages[
                        Random.Shared.Next(
                            Strings.ErrorMessages.Length)];

                await ch.SendMessageAsync(msg);
            }
        }
        catch
        {
            // 送信失敗は無視
        }
        Logger.Error(ex, "エラー報告");
    }

    // --- 安全な送受信ヘルパー ---
    private async Task SafeSendChannelMessageAsync(ulong channelId, string content)
    {
        try
        {
            var ch = await _client.GetChannelAsync(channelId);
            if (ch != null)
                await ch.SendMessageAsync(content);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "メッセージ送信失敗");
        }
    }

    private async Task SafeAppendToMessageAsync(ulong channelId, ulong messageId, string append)
    {
        try
        {
            var ch = await _client.GetChannelAsync(channelId);
            if (ch == null) return;

            var msg = await ch.GetMessageAsync(messageId);
            if (msg == null) return;

            await msg.ModifyAsync(m =>
            {
                m.Content = (msg.Content ?? string.Empty) + append;
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "メッセージ追記失敗");
        }
    }

    private async Task SafeDeleteMessageAsync(ulong channelId, ulong messageId)
    {
        try
        {
            var ch = await _client.GetChannelAsync(channelId);
            if (ch == null) return;

            var msg = await ch.GetMessageAsync(messageId);
            if (msg == null) return;

            await msg.DeleteAsync();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "メッセージ削除失敗");
        }
    }

    private async Task SafeCreateResponseAsync(ComponentInteractionCreateEventArgs e, string content)
    {
        try
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(content)
                    .AsEphemeral(true));
        }
        catch (Exception ex)
        {
            // ここでは二重応答などの失敗をログに残す
            Logger.Error(ex, "応答作成失敗");
        }
    }

    private async Task SafeCreateFollowupAsync(ComponentInteractionCreateEventArgs e, string content)
    {
        try
        {
            await e.Interaction.CreateFollowupMessageAsync(
                new DiscordFollowupMessageBuilder()
                    .WithContent(content)
                    .AsEphemeral(true));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "フォローアップ作成失敗");
        }
    }
}