using System;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Contexts;
using TacoDiscordBot.Services;

namespace TacoDiscordBot.Commands;

public class BoCommands : ApplicationCommandModule
{
    [SlashCommand("bo", "募集を作成します")]
    public async Task Bo(
        InteractionContext ctx,
        [Option("content", "募集内容 (任意)")] string content = "",
        [Option("at", "募集人数（募集主含めない。例: @3 は募集主＋3人）(任意)")] long at = 0,
        [Option("rank", "ランク(任意)")] string rank = "",
        [Option("deadline", "締め切り（任意、例: 2026-08-13 01:30）")] string deadline = "",
        [Option("description", "募集の説明（任意）")] string description = ""
    )
    {
        await BoAsync(
            new InteractionResponseContext(ctx),
            ctx,
            content,
            at,
            rank,
            deadline,
            description,
            BotHost.BoManager
        );
    }

    public async Task BoAsync(
        IInteractionResponseContext response,
        InteractionContext context,
        string content = "",
        long at = 0,
        string rank = "",
        string deadline = "",
        string description = "",
        IBoManager manager = null
    )
    {
        manager ??= BotHost.BoManager;

        if (manager == null)
        {
            await response.RespondAsync("募集サービスは未設定です。", true);
            return;
        }

        await response.DeferResponseAsync();

        DateTime? parsedDeadline = null;

        if (!string.IsNullOrWhiteSpace(deadline))
        {
            // 締め切りは yyyy-MM-dd HH:mm の形式で入力します。
            // 入力された日時は日本時間（JST）として扱います。
            if (
                !DateTime.TryParseExact(
                    deadline.Trim(),
                    "yyyy-MM-dd HH:mm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var parsed
                )
            )
            {
                await response.EditResponseAsync(Strings.BoDeadlineInvalid);

                return;
            }

            // 環境の Local / UTC に依存しないように Unspecified にします。
            parsedDeadline = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        }

        await manager.CreateSessionAsync(
            context,
            content,
            (int)at,
            rank,
            parsedDeadline,
            description
        );

        // 保留中のレスポンスを編集して確定します。
        await response.EditResponseAsync(Strings.BoCreatedConfirmation);
    }
}
