using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq; // パッチの位置合わせのためのプレースホルダ（無害）
using System.Globalization;
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
    // BO（募集）管理サービス。
    // メモリ上でセッションを管理し、永続化はオプションで BoRepository を通じて行います。
    private readonly DiscordClient _client;
    private readonly ConcurrentDictionary<string, Models.BoSession> _sessions = new();
    private readonly DeadlineService _deadlineService;
    private readonly BoRepository _repo;

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
    internal async Task<bool> ApplyDeadlineToLatestSessionAsync(ulong userId, DateTime utcDeadline, string raw)
    {
        try
        {
            var session = _sessions.Values
                .Where(x => x.OwnerId == userId && !x.IsClosed)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefault();

            if (session == null)
                return false;

            session.Deadline = utcDeadline;
            session.DeadlineRaw = raw;

            try
            {
                await UpdateSessionMessageAsync(session);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "ApplyDeadline: メッセージ更新失敗");
            }

            // 永続化を試みる
            try
            {
                if (_repo != null)
                    await _repo.UpdateSessionAsync(session);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "ApplyDeadline: 永続化失敗");
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "ApplyDeadlineToLatestSessionAsync");
            return false;
        }
    }

    private static BoRepository CreateFromEnvOrNull()
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
            return new BoRepository(baseRepo);
        }
        catch
        {
            return null;
        }
    }

    // BOセッションはメモリ管理とします。
    // 永続化は行いません。
    public BoManager(DiscordClient client, BoRepository repo = null)
    {
        _client = client;

        // ロガー出力: コンストラクタ開始
        Logger.Info("BoManager: 初期化開始");

        // リポジトリは DI（起動時）で渡される場合があります。渡されない場合は環境変数からフォールバックを試みます。
        _repo = repo ?? CreateFromEnvOrNull();
        if (_repo != null)
        {
            try
            {
                var persisted = _repo.LoadActiveSessionsAsync().GetAwaiter().GetResult();
                foreach (var s in persisted)
                {
                    _sessions[s.SessionId] = s;
                }
                Logger.Info($"BoManager: DB からセッションを読み込み 件数={persisted.Count}");
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
        Logger.Info("BoManager: 期限切れクリーンアップタスクを開始");

        // 締め切り用サービスを初期化
        _deadlineService = new DeadlineService(this);

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

        // 締め切りコンポーネントは DeadlineService に委譲
        if (id.StartsWith("deadline_"))
        {
            try
            {
                var handled = await _deadlineService.HandleInteractionAsync(e);
                if (handled)
                    return;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "deadline service handling");
            }
        }

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

            // Interaction を ACK（失敗しても処理は続行）
            try
            {
                await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            }
            catch
            {
                // ACK に失敗しても無視します
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
            // 永続化: 終了状態を保存
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
        // 永続化: 終了状態を保存
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
            // Embed（条件に応じてフィールドを追加）
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
                        Strings.LabelDeadline,
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
                    Strings.LabelDeadline,
                    session.DeadlineRaw,
                    false);
            }

            // 説明（指定があれば表示）
            if (!string.IsNullOrWhiteSpace(session.Description))
            {
                embedBuilder.AddField(
                    Strings.LabelDescription,
                    session.Description,
                    false);
            }

            // 参加者一覧（常に表示）
            embedBuilder.AddField(
                Strings.ParticipantsFieldPrefix + Strings.LabelParticipants,
                participantsText,
                false);

            // フッターは募集人数が指定されている場合のみ表示
            if (session.At > 0)
            {
                embedBuilder.WithFooter(Strings.FooterParticipantCount + atText);
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
                content = string.Format(Strings.ContentMinimalTemplate, session.OwnerId);
            }
            else
            {
                // 募集内容が指定されている場合は「○○さんが<募集内容>の募集を開始しました！」を表示
                if (!string.IsNullOrWhiteSpace(session.Body))
                    content = string.Format(Strings.ContentWithBodyTemplate, session.OwnerId, session.Body);
                else
                    content = Strings.EmbedStartContent;
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
                                Strings.ButtonJoinLabel),

                            new DiscordButtonComponent(
                                ButtonStyle.Secondary,
                                $"bo_cancel:{sessionId}",
                                Strings.ButtonCancelParticipationLabel),

                            new DiscordButtonComponent(
                                ButtonStyle.Danger,
                                $"bo_close:{sessionId}",
                                Strings.ButtonCloseLabel)
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
                // 永続ストレージからも削除
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
            // Embed（条件に応じてフィールドを追加）
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
