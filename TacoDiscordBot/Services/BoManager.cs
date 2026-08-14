using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq; // No-op placeholder to ensure patch sections align
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Models;
using TacoDiscordBot.Util;
using TacoDiscordBot.Repository;

namespace TacoDiscordBot.Services;

public class BoManager
{
    private readonly DiscordClient _client;
    private readonly ConcurrentDictionary<string, Models.BoSession> _sessions = new();
    private readonly BoRepository _repo;

    // BOセッションはメモリ管理とします。
    // 永続化は行いません。
    public BoManager(DiscordClient client)
    {
        _client = client;

        // Try to initialize repository (Postgres) and load persisted sessions
        _repo = BoRepository.TryCreateFromEnv();
        if (_repo != null)
        {
            try
            {
                var persisted = _repo.LoadActiveSessionsAsync().GetAwaiter().GetResult();
                foreach (var s in persisted)
                {
                    _sessions[s.SessionId] = s;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "DB からのセッション読み込みに失敗");
            }
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

                            // 締め切りメッセージにも募集内容が分かるようにする
                            var header = !string.IsNullOrWhiteSpace(session.Body)
                                ? $"📢 『{session.Body}』 の募集は締め切りです！"
                                : "📢 募集は締め切りです！";

                            if (string.IsNullOrWhiteSpace(mentions))
                            {
                                _ = SafeSendChannelMessageAsync(session.ChannelId, header);
                            }
                            else
                            {
                                _ = SafeSendChannelMessageAsync(session.ChannelId, header + " " + mentions);
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
                // どの募集が終了したか分かるように、募集内容（ある場合）を含めて通知
                if (!string.IsNullOrWhiteSpace(session.Body))
                    await ch.SendMessageAsync($"📢 募集を終了しました：『{session.Body}』");
                else
                    await ch.SendMessageAsync("📢 募集を終了しました。");

            var msg = await ch.GetMessageAsync(session.MessageId);
            if (msg != null)
            {
                await msg.ModifyAsync(m => { m.Content = (msg.Content ?? string.Empty) + "\n\n**（募集終了）**"; });
            }
            // persist close
            try
            {
                if (_repo != null)
                    await _repo.UpdateSessionAsync(session);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "セッション更新(終了) 永続化失敗");
            }
        }
            // persist close
            try
            {
                if (_repo != null)
                    await _repo.UpdateSessionAsync(session);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "セッション更新(終了) 永続化失敗");
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
                // どの募集で人数が集まったか分かるように募集内容を含める
                if (!string.IsNullOrWhiteSpace(session.Body))
                    await ch.SendMessageAsync($"📢 人数が集まりました！（募集: 『{session.Body}』） {mentions}");
                else
                    await ch.SendMessageAsync($"📢 人数が集まりました！ {mentions}");

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
        string body,
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
                Body = body,
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
            // Embed (条件付きでフィールドを追加)
            // ==============================
            var embedBuilder =
                new DiscordEmbedBuilder()
                    .WithTitle(Strings.EmbedTitle)
                    .WithColor(DiscordColor.Blurple)
                    .WithTimestamp(DateTime.UtcNow);

            // 募集内容は指定されていれば表示
            if (!string.IsNullOrWhiteSpace(session.Body))
            {
                embedBuilder.AddField(
                    "🎮 " + Strings.LabelContent,
                    session.Body,
                    false);
            }

            // 募集主は常に表示
            embedBuilder.AddField(
                "👤 " + Strings.LabelOwner,
                $"<@{session.OwnerId}>",
                false);

            // ランク（指定があれば表示）
            if (!string.IsNullOrWhiteSpace(session.Rank))
            {
                embedBuilder.AddField(
                    "🏅 " + Strings.LabelRank,
                    session.Rank,
                    false);
            }

            // 締め切り（締め切りがある、または生の入力がある場合に表示）
            if (session.Deadline.HasValue)
            {
                embedBuilder.AddField(
                    "⏰ 締切",
                    TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.SpecifyKind(
                            session.Deadline.Value,
                            DateTimeKind.Utc),
                        TimeZoneInfo.FindSystemTimeZoneById(
                            "Tokyo Standard Time"))
                        .ToString(Strings.DateTimeFormat),
                    false);
            }
            else if (!string.IsNullOrWhiteSpace(session.DeadlineRaw))
            {
                embedBuilder.AddField(
                    "⏰ 締切",
                    session.DeadlineRaw,
                    false);
            }

            // 説明（指定があれば表示）
            if (!string.IsNullOrWhiteSpace(session.Description))
            {
                embedBuilder.AddField(
                    "📝 説明",
                    session.Description,
                    false);
            }

            // 参加者一覧（常に表示）
            embedBuilder.AddField(
                "📋 " + Strings.LabelParticipants,
                participantsText,
                false);

            // フッターは募集人数が指定されている場合のみ表示
            if (session.At > 0)
            {
                embedBuilder.WithFooter("参加数: " + atText);
            }

            var embed = embedBuilder;

            // ==============================
            // 募集メッセージ本体（簡易表示判定）
            // ==============================
            var isMinimal =
                string.IsNullOrWhiteSpace(session.Body) &&
                session.At == 0 &&
                string.IsNullOrWhiteSpace(session.Rank) &&
                string.IsNullOrWhiteSpace(session.DeadlineRaw) &&
                string.IsNullOrWhiteSpace(session.Description);

            string content;
            if (isMinimal)
            {
                // 他項目が未指定のみのシンプルな投稿
                content = $"@here\n<@{session.OwnerId}>さんが何か募集しているようです";
            }
            else
            {
                // 募集内容が指定されている場合は「○○さんが<募集内容>の募集を開始しました！」を表示
                if (!string.IsNullOrWhiteSpace(session.Body))
                    content = $"@here\n<@{session.OwnerId}>さんが{session.Body}の募集を開始しました！";
                else
                    content = $"@here\n募集を開始しました！";
            }

            // ==============================
            // 募集メッセージ
            // ==============================
            var builder =
                new DiscordMessageBuilder()
                    .WithContent(content)
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

            // 埋め込みは常に追加する（最小表示でも詳細は埋め込みで確認できるように）
            builder.AddEmbed(embed);

            var msg =
                await ctx.Channel.SendMessageAsync(
                    builder);

            // DiscordメッセージIDを確定
            session.MessageId = msg.Id;

            // セッションをメモリに保存
            _sessions[sessionId] = session;
            // 永続化
            try
            {
                if (_repo != null)
                    await _repo.CreateSessionAsync(session);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "セッション永続化失敗");
            }
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
                // delete from persistent storage as well
                try
                {
                    if (_repo != null)
                        await _repo.DeleteSessionAsync(session.SessionId);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "セッション削除 永続化失敗");
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
            // Embed (条件付きでフィールドを追加)
            // ==============================
            var embedBuilder =
                new DiscordEmbedBuilder()
                    .WithTitle(Strings.EmbedTitle)
                    .WithColor(DiscordColor.Blurple)
                    .WithTimestamp(DateTime.UtcNow);

            // 募集内容
            if (!string.IsNullOrWhiteSpace(session.Body))
            {
                embedBuilder.AddField(
                    "🎮 " + Strings.LabelContent,
                    session.Body,
                    false);
            }

            // 募集主
            embedBuilder.AddField(
                "👤 " + Strings.LabelOwner,
                $"<@{session.OwnerId}>",
                false);

            // ランク（指定があれば表示）
            if (!string.IsNullOrWhiteSpace(session.Rank))
            {
                embedBuilder.AddField(
                    "🏅 " + Strings.LabelRank,
                    session.Rank,
                    false);
            }

            // 締め切り（締め切りがある、または生の入力がある場合に表示）
            if (session.Deadline.HasValue)
            {
                embedBuilder.AddField(
                    "⏰ 締切",
                    TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.SpecifyKind(
                            session.Deadline.Value,
                            DateTimeKind.Utc),
                        TimeZoneInfo.FindSystemTimeZoneById(
                            "Tokyo Standard Time"))
                        .ToString(Strings.DateTimeFormat),
                    false);
            }
            else if (!string.IsNullOrWhiteSpace(session.DeadlineRaw))
            {
                embedBuilder.AddField(
                    "⏰ 締切",
                    session.DeadlineRaw,
                    false);
            }

            // 説明（指定があれば表示）
            if (!string.IsNullOrWhiteSpace(session.Description))
            {
                embedBuilder.AddField(
                    "📝 説明",
                    session.Description,
                    false);
            }

            // 参加者一覧
            embedBuilder.AddField(
                "📋 " + Strings.LabelParticipants,
                participantsText,
                false);

            // フッターは募集人数が指定されている場合のみ表示
            if (session.At > 0)
            {
                embedBuilder.WithFooter("参加数: " + atText);
            }

            var embed = embedBuilder;

            // ==============================
            // メッセージ（簡易表示判定）
            // ==============================
            var isMinimal =
                string.IsNullOrWhiteSpace(session.Body) &&
                session.At == 0 &&
                string.IsNullOrWhiteSpace(session.Rank) &&
                string.IsNullOrWhiteSpace(session.DeadlineRaw) &&
                string.IsNullOrWhiteSpace(session.Description);

            string content;
            if (isMinimal)
            {
                content = $"@here\n<@{session.OwnerId}>さんが何か募集しているようです";
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(session.Body))
                    content = $"@here\n<@{session.OwnerId}>さんが{session.Body}の募集を更新しました！";
                else
                    content = $"@here\n募集を更新しました！";
            }

            // ==============================
            // メッセージ
            // ==============================
            var builder =
                new DiscordMessageBuilder()
                    .WithContent(content)
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

            // 埋め込みは常に追加する（最小表示でも詳細は埋め込みで確認できるように）
            builder.AddEmbed(embed);

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
                            Logger.Error(ex, "メッセージ編集失敗");
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
