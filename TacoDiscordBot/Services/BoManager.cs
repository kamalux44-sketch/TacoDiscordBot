using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
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
    }

    public async Task HandleComponentInteraction(DiscordClient client, ComponentInteractionCreateEventArgs e)
    {
        var id = e.Id;
        if (string.IsNullOrEmpty(id)) return;

        if (id.StartsWith("bo_join:") || id.StartsWith("bo_cancel:"))
        {
            var parts = id.Split(':', 2);
            if (parts.Length != 2) return;
            var action = parts[0];
            var sessionId = parts[1];
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                // session not found - respond ephemerally so the user does not see an interaction timeout
                try
                {
                    await e.Interaction.CreateResponseAsync(DSharpPlus.InteractionResponseType.ChannelMessageWithSource,
                        new DSharpPlus.Entities.DiscordInteractionResponseBuilder().WithContent("募集が見つかりませんでした。").AsEphemeral(true));
                }
                catch
                {
                    // ignore
                }
                return;
            }

            try
            {
                // Acknowledge the interaction quickly to avoid the Discord "application did not respond" message.
                // We defer an update because we will edit the original message below.
                try
                {
                    await e.Interaction.CreateResponseAsync(DSharpPlus.InteractionResponseType.DeferredMessageUpdate);
                }
                catch
                {
                    // If acknowledgement fails, continue - we still attempt to update the message.
                }

                lock (session)
                {
                    if (action == "bo_join")
                    {
                        if (!session.Participants.Contains(e.User.Id))
                            session.Participants.Add(e.User.Id);
                    }
                    else if (action == "bo_cancel")
                    {
                        session.Participants.RemoveAll(x => x == e.User.Id);
                    }
                }

                await UpdateSessionMessageAsync(session);
            }
            catch (Exception ex)
            {
                // コマンドが打たれたチャンネル（募集の投稿チャンネル）にエラー文言を通知
                try
                {
                    await ReportErrorAsync(session.ChannelId, ex);
                }
                catch
                {
                    // ここでは二次エラーは無視する
                }
            }
        }
    }

    public async Task CreateSessionAsync(InteractionContext ctx, string game, int at, string rank)
    {
        try
        {
            var sessionId = Guid.NewGuid().ToString();

            // セッションを先に作成して募集主を参加者リストの先頭に入れます。
            var session = new Models.BoSession
            {
                SessionId = sessionId,
                MessageId = 0, // 後で設定
                ChannelId = ctx.Channel.Id,
                Game = game,
                At = at,
                Rank = rank ?? string.Empty,
                OwnerId = ctx.User.Id,
                Participants = new List<ulong> { ctx.User.Id },
                CreatedAt = DateTime.UtcNow
            };

            // 埋め込みメッセージを装飾して作成します。
            var participantsText = string.Join("\n", session.Participants.Select((id, idx) => $"{idx + 1}. <@{id}>") );
            if (string.IsNullOrEmpty(participantsText)) participantsText = "—";
            var atText = session.At > 0 ? $"{session.Participants.Count}/{session.At}" : $"{session.Participants.Count}/任意";

            var embed = new DiscordEmbedBuilder()
                .WithTitle(Strings.EmbedTitle)
                .AddField("🎮 " + Strings.LabelGame, string.IsNullOrWhiteSpace(session.Game) ? "未設定" : session.Game, false)
                .AddField("👤 " + Strings.LabelOwner, $"<@{session.OwnerId}>", true)
                .AddField("🏅 " + Strings.LabelRank, string.IsNullOrEmpty(session.Rank) ? "未設定" : session.Rank, true)
                .AddField("📋 " + Strings.LabelParticipants, participantsText, false)
                .WithFooter(atText)
                .WithColor(DiscordColor.Blurple)
                .WithTimestamp(DateTime.UtcNow);

            var builder = new DiscordMessageBuilder()
                .WithContent(Strings.EmbedStartContent)
                .AddEmbed(embed)
                .AddComponents(new DiscordComponent[] {
                    new DiscordButtonComponent(ButtonStyle.Primary, $"bo_join:{sessionId}", Strings.ButtonJoin),
                    new DiscordButtonComponent(ButtonStyle.Secondary, $"bo_cancel:{sessionId}", Strings.ButtonCancel)
                });

            var msg = await ctx.Channel.SendMessageAsync(builder);

            // メッセージ ID を確定してセッションを格納
            session.MessageId = msg.Id;
            _sessions[sessionId] = session;
        }
        catch (Exception ex)
        {
            // コマンドが打たれたチャンネルにエラーメッセージを通知し、コンソールに詳細を出力します。
            try
            {
                var msg = Strings.ErrorMessages[Random.Shared.Next(Strings.ErrorMessages.Length)];
                await ctx.Channel.SendMessageAsync(msg);
            }
            catch
            {
                // 通知に失敗しても無視
            }
            Console.WriteLine(ex.ToString());
        }
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
                    // メッセージ削除を試みる（失敗しても報告して続行）
                    var ch = await _client.GetChannelAsync(session.ChannelId);
                    if (ch != null)
                    {
                        var msg = await ch.GetMessageAsync(session.MessageId);
                        if (msg != null)
                        {
                            await msg.DeleteAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // エラーをコマンドチャンネル（募集投稿チャンネル）に通知し、コンソールに詳細を出力
                    try
                    {
                        await ReportErrorAsync(session.ChannelId, ex);
                    }
                    catch
                    {
                        // 通知失敗は無視
                    }
                }
            }
        }
    }

    private async Task UpdateSessionMessageAsync(Models.BoSession session)
    {
        try
        {
            // 参加者一覧と参加数を組み立てます。
            var participantsText = session.Participants.Count == 0 ? "—" : string.Join("\n", session.Participants.Select((id, idx) => $"{idx + 1}. <@{id}>"));
            var cur = session.Participants.Count;
            var atText = session.At > 0 ? $"{cur}/{session.At}" : $"{cur}/任意";

            var embed = new DiscordEmbedBuilder()
                .WithTitle(Strings.EmbedTitle)
                .AddField("\uD83C\uDFAE " + Strings.LabelGame, string.IsNullOrWhiteSpace(session.Game) ? "未設定" : session.Game, false)
                .AddField("\uD83D\uDC64 " + Strings.LabelOwner, $"<@{session.OwnerId}>", true)
                .AddField("\uD83C\uDFC5 " + Strings.LabelRank, string.IsNullOrEmpty(session.Rank) ? "未設定" : session.Rank, true)
                .AddField("\uD83D\uDCCB " + Strings.LabelParticipants, participantsText, false)
                .WithFooter(atText)
                .WithColor(DiscordColor.Blurple)
                .WithTimestamp(DateTime.UtcNow);

            var builder = new DiscordInteractionResponseBuilder()
                .WithContent(Strings.EmbedUpdatedContent)
                .AddEmbed(embed)
                .AddComponents(new DiscordComponent[] {
                    new DiscordButtonComponent(ButtonStyle.Primary, $"bo_join:{session.SessionId}", Strings.ButtonJoin),
                    new DiscordButtonComponent(ButtonStyle.Secondary, $"bo_cancel:{session.SessionId}", Strings.ButtonCancel)
                });

            // Edit the original message to reflect updated participants
            var ch = await _client.GetChannelAsync(session.ChannelId);
            if (ch != null)
            {
                var msg = await ch.GetMessageAsync(session.MessageId);
                if (msg != null)
                {
                    await msg.ModifyAsync(m =>
                    {
                        m.Content = builder.Content;
                        m.Embed = embed;
                        // keep existing components (buttons) as they were
                    });
                }
            }
        }
        catch (Exception ex)
        {
            // エラー発生時は募集のチャンネルに通知し、スタックトレースをコンソールに出力します。
            try
            {
                await ReportErrorAsync(session.ChannelId, ex);
            }
            catch
            {
                // 通知失敗は無視
            }
            Console.WriteLine(ex.ToString());
        }
    }

    /// <summary>
    /// 指定チャンネルへエラー文言を送信し、コンソールに例外情報を出力します。
    /// 失敗しても例外は投げません（呼び出し側は継続します）。
    /// </summary>
    private async Task ReportErrorAsync(ulong channelId, Exception ex)
    {
        try
        {
            var ch = await _client.GetChannelAsync(channelId);
            if (ch != null)
            {
                var msg = Strings.ErrorMessages[Random.Shared.Next(Strings.ErrorMessages.Length)];
                await ch.SendMessageAsync(msg);
            }
        }
        catch
        {
            // 送信失敗は無視
        }

        // コンソールにはスタックトレースを出力する
        Console.WriteLine(ex.ToString());
    }

}
