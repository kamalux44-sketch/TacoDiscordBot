using System;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;

namespace TacoDiscordBot.Commands;

public class BoCommands : ApplicationCommandModule
{
    [SlashCommand("bo", "募集を作成します")]
    public async Task Bo(
        InteractionContext ctx,

        [Option("content", "募集内容 (任意)")]
        string content = "",

        [Option(
            "at",
            "募集人数（募集主含めない。例: @3 は募集主＋3人）(任意)")]
        long at = 0,

        [Option(
            "rank",
            "ランク(任意)")]
        string rank = "",

        [Option(
            "deadline",
            "締め切り（任意、例: 2026-08-13 01:30）")]
        string deadline = "",

        [Option(
            "description",
            "募集の説明（任意）")]
        string description = "")
    {
        DateTime? parsedDeadline = null;

        if (!string.IsNullOrWhiteSpace(deadline))
        {
            // 締め切りは「yyyy-MM-dd HH:mm」の形式で入力します。
            //
            // 入力例：
            // 2026-08-13 01:30
            //
            // この日時は必ず日本時間（JST）として扱います。
            if (!DateTime.TryParseExact(
                    deadline.Trim(),
                    "yyyy-MM-dd HH:mm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var parsed))
            {
                await ctx.CreateResponseAsync(
                    InteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder()
                        .WithContent(
                            "締め切りの日時が正しくありません。\n" +
                            "以下の形式で入力してください：\n" +
                            "`2026-08-13 01:30`")
                        .AsEphemeral(true));

                return;
            }

            // 入力された日時を「日本時間」として扱います。
            //
            // Local / UTCなどの環境依存を避けるため、
            // DateTimeKind.Unspecified にします。
            parsedDeadline = DateTime.SpecifyKind(
                parsed,
                DateTimeKind.Unspecified);
        }

        await BotHost.BoManager.CreateSessionAsync(
            ctx,
            content,
            (int)at,
            rank,
            parsedDeadline,
            description);
    }
}
