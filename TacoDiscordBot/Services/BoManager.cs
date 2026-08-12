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

namespace TacoDiscordBot.Services;

public class BoManager
{
    private readonly DiscordClient _client;
    private readonly ConcurrentDictionary<string, Models.BoSession> _sessions = new();

    // 日本時間（JST / UTC+9）を固定で使用します。
    // Windowsでは "Tokyo Standard Time" が日本標準時を表します。
    private static readonly TimeZoneInfo JapanTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");

    // BO セッションはメモリ管理とします。永続化は行いません。

    public BoManager(DiscordClient client)
    {
        _client = client;

        // バックグラウンドで定期的に古い募集をクリーンアップします。
        // ここでは1時間ごとにチェックし、作成から1週間を超えた募集を破棄します。
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
                    // 例外はログ出力して無視し、ループを継続します。
                    Console.WriteLine(ex.ToString());
                }

                await Task.Delay(TimeSpan.FromHours(1));
            }
        });

        // 締め切りチェック用の定期タスク（1分毎）
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    // 現在時刻はUTCで取得します。
                    // DeadlineもUTCで保存しているため、直接比較できます。
                    var nowUtc = DateTime.UtcNow;

                    foreach (var kv in _sessions)
                    {
                        var s = kv.Value;

                        if (s == null)
                            continue;

                        if (s.IsClosed)
                            continue;

                        if (s.Deadline.HasValue && nowUtc >= s.Deadline.Value)
                        {
                            // 二重通知防止
                            s.IsClosed = true;

                            try
                            {
                                var ch = await _client.GetChannelAsync(s.ChannelId);

                                if (ch != null)
                                {
                                    var mentions =
                                        s.Participants != null &&
                                        s.Participants.Count > 0
                                            ? string.Join(
                                                " ",
                                                s.Participants.Select(id => $"<@{id}>"))
                                            : string.Empty;

                                    await ch.SendMessageAsync(
                                        $"締め切りです！ {mentions}");

                                    // 元の募集メッセージに締め切り済みと追記
                                    try
                                    {
                                        var msg = await ch.GetMessageAsync(s.MessageId);

                                        if (msg != null)
                                        {
                                            await msg.ModifyAsync(m =>
                                            {
                                                m.Content =
                                                    (msg.Content ?? string.Empty) +
                                                    "\n\n**（締め切り済み）**";
                                            });
                                        }
                                    }
                                    catch
                                    {
                                        // メッセージ編集失敗は無視
                                    }
                                }
                            }
                            catch
                            {
                                // 締め切り通知失敗は無視
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }

                await Task.Delay(TimeSpan.FromMinutes(1));
            }
        });
    }

    public async Task HandleComponentInteraction(
        DiscordClient client,
        ComponentInteractionCreateEventArgs e)
    {
        var id = e.Id;

        if (string.IsNullOrEmpty(id))
            return;

        if (id.StartsWith("bo_join:") || id.StartsWith("bo_cancel:"))
        {
            var parts = id.Split(':', 2);

            if (parts.Length != 2)
                return;

            var action = parts[0];
            var sessionId = parts[1];

            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                // セッションが見つからない場合、
                // Interaction Timeoutを防ぐためEphemeralで応答します。
                try
                {
                    await e.Interaction.CreateResponseAsync(
                        DSharpPlus.InteractionResponseType.ChannelMessageWithSource,
                        new DSharpPlus.Entities.DiscordInteractionResponseBuilder()
                            .WithContent("募集が見つかりませんでした。")
                            .AsEphemeral(true));
                }
                catch
                {
                    // 無視
                }

                return;
            }

            try
            {
                // Interactionを先にACKします。
                try
                {
                    await e.Interaction.CreateResponseAsync(
                        DSharpPlus.InteractionResponseType.DeferredMessageUpdate);
                }
                catch
                {
                    // ACK失敗しても更新処理は続行します。
                }

                // 更新前の人数
                var prevCount = session.Participants.Count;

                lock (session)
                {
                    if (action == "bo_join")
                    {
                        if (!session.Participants.Contains(e.User.Id))
                        {
                            session.Participants.Add(e.User.Id);
                        }
                    }
                    else if (action == "bo_cancel")
                    {
                        session.Participants.RemoveAll(x => x == e.User.Id);
                    }
                }

                await UpdateSessionMessageAsync(session);

                // 定員に達した場合、参加者全員に通知
                try
                {
                    var cur = session.Participants.Count;

                    var capacity =
                        session.At > 0
                            ? session.At + 1
                            : int.MaxValue;

                    if (
                        session.At > 0 &&
                        prevCount < capacity &&
                        cur >= capacity)
                    {
                        var ch =
                            await _client.GetChannelAsync(session.ChannelId);

                        if (ch != null)
                        {
                            var mentions = string.Join(
                                " ",
                                session.Participants.Select(
                                    id => $"<@{id}>"));

                            await ch.SendMessageAsync(
                                $"人数が集まりました！ {mentions}");
                        }
                    }
                }
                catch
                {
                    // 通知失敗は無視
                }
            }
            catch (Exception ex)
            {
                // コマンドが打たれたチャンネルにエラー通知
                try
                {
                    await ReportErrorAsync(session.ChannelId, ex);
                }
                catch
                {
                    // 二次エラーは無視
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

            /*
             * deadlineは「日本時間」として扱います。
             *
             * DiscordのSlashCommand等から渡されたDateTimeが
             * Unspecified / Local / UTC のどれであっても、
             * ここでは「入力された年月日時分そのものをJST」として扱います。
             *
             * その後UTCへ変換して保存します。
             */
            DateTime? deadlineUtc = null;
            string deadlineRaw = string.Empty;

            if (deadline.HasValue)
            {
                // DateTimeのKindを無視し、
                // 入力された年月日時分を「日本時間」として再定義します。
                var deadlineJst = DateTime.SpecifyKind(
                    deadline.Value,
                    DateTimeKind.Unspecified);

                // 日本時間 → UTC
                deadlineUtc = TimeZoneInfo.ConvertTimeToUtc(
                    deadlineJst,
                    JapanTimeZone);

                // 表示用は常に日本時間
                deadlineRaw = deadlineJst.ToString(
                    Strings.DateTimeFormat);
            }

            // セッションを先に作成して募集主を参加者リストの先頭に入れます。
            var session = new Models.BoSession
            {
                SessionId = sessionId,

                // 後で設定
                MessageId = 0,

                ChannelId = ctx.Channel.Id,
                Game = game,
                At = at,
                Rank = rank ?? string.Empty,

                // 表示用締切
                DeadlineRaw = deadlineRaw,

                Description = description ?? string.Empty,
                OwnerId = ctx.User.Id,

                Participants = new List<ulong>
                {
                    ctx.User.Id
                },

                CreatedAt = DateTime.UtcNow,
                IsClosed = false,

                // 締切はUTCで保存
                Deadline = deadlineUtc
            };

            // 埋め込みメッセージ用の参加者一覧
            var participantsText = string.Join(
                "\n",
                session.Participants.Select(
                    (id, idx) => $"{idx + 1}. <@{id}>"));

            if (string.IsNullOrEmpty(participantsText))
            {
                participantsText = "—";
            }

            var atText =
                session.At > 0
                    ? $"{session.Participants.Count}/{session.At + 1}"
                    : $"{session.Participants.Count}/任意";

            // 締切表示
            var deadlineText = GetDeadlineDisplayText(session);

            var embed = new DiscordEmbedBuilder()
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
                    deadlineText,
                    true)
                .AddField(
                    "📝 説明",
                    string.IsNullOrWhiteSpace(session.Description)
                        ? "—"
                        : session.Description,
                    false)
                .AddField(
                    "📋 " + Strings.LabelParticipants,
                    participantsText,
                    false)
                .WithFooter("参加数: " + atText)
                .WithColor(DiscordColor.Blurple)
                .WithTimestamp(DateTime.UtcNow);

            var builder = new DiscordMessageBuilder()
                .WithContent(
                    $"@here\n**{(
                        string.IsNullOrWhiteSpace(session.Game)
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
                            Strings.ButtonJoin),

                        new DiscordButtonComponent(
                            ButtonStyle.Secondary,
                            $"bo_cancel:{sessionId}",
                            Strings.ButtonCancel)
                    });

            var msg = await ctx.Channel.SendMessageAsync(builder);

            // メッセージIDを確定してセッションを格納
            session.MessageId = msg.Id;
            _sessions[sessionId] = session;
        }
        catch (Exception ex)
        {
            // コマンドが打たれたチャンネルにエラーメッセージを通知
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

            Console.WriteLine(ex.ToString());
        }
    }

    /// <summary>
    /// セッションに保存されているUTCの締切を
    /// 日本時間（JST）に変換して表示用文字列を返します。
    /// </summary>
    private static string GetDeadlineDisplayText(
        Models.BoSession session)
    {
        if (!session.Deadline.HasValue)
        {
            return string.IsNullOrWhiteSpace(session.DeadlineRaw)
                ? "—"
                : session.DeadlineRaw;
        }

        // UTC → JST
        var deadlineJst = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(
                session.Deadline.Value,
                DateTimeKind.Utc),
            JapanTimeZone);

        return deadlineJst.ToString(
            Strings.DateTimeFormat);
    }

    /// <summary>
    /// 作成から7日を超えた募集を破棄します。
    /// メッセージは削除し、メモリ上のセッション情報を破棄します。
    /// </summary>
    private async Task CleanExpiredSessionsAsync()
    {
        var expiry = TimeSpan.FromDays(7);
        var now = DateTime.UtcNow;

        var toRemove = new List<string>();

        foreach (var kv in _sessions)
        {
            var s = kv.Value;

            if (now - s.CreatedAt > expiry)
            {
                toRemove.Add(kv.Key);
            }
        }

        foreach (var id in toRemove)
        {
            if (_sessions.TryRemove(id, out var session))
            {
                try
                {
                    // メッセージ削除を試みます。
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
                    // エラーを募集投稿チャンネルに通知
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
                }
            }
        }
    }

    private async Task UpdateSessionMessageAsync(
        Models.BoSession session)
    {
        try
        {
            // 参加者一覧
            var participantsText =
                session.Participants.Count == 0
                    ? "—"
                    : string.Join(
                        "\n",
                        session.Participants.Select(
                            (id, idx) =>
                                $"{idx + 1}. <@{id}>"));

            var cur = session.Participants.Count;

            var atText =
                session.At > 0
                    ? $"{cur}/{session.At + 1}"
                    : $"{cur}/任意";

            // 締切表示
            var deadlineText =
                GetDeadlineDisplayText(session);

            var embed = new DiscordEmbedBuilder()
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
                    deadlineText,
                    true)
                .AddField(
                    "📝 説明",
                    string.IsNullOrWhiteSpace(session.Description)
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

            var builder =
                new DiscordInteractionResponseBuilder()
                    .WithContent(
                        $"@here\n**{(
                            string.IsNullOrWhiteSpace(session.Game)
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
                                Strings.ButtonJoin),

                            new DiscordButtonComponent(
                                ButtonStyle.Secondary,
                                $"bo_cancel:{session.SessionId}",
                                Strings.ButtonCancel)
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
                    await msg.ModifyAsync(m =>
                    {
                        m.Content = builder.Content;
                        m.Embed = embed;

                        // ボタンは既存のものを維持します。
                    });
                }
            }
        }
        catch (Exception ex)
        {
            // エラーを募集チャンネルに通知
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

            Console.WriteLine(ex.ToString());
        }
    }

    /// <summary>
    /// 指定チャンネルへエラー文言を送信し、
    /// コンソールには例外情報を出力します。
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

        // コンソールにはスタックトレースを出力
        Console.WriteLine(ex.ToString());
    }
}