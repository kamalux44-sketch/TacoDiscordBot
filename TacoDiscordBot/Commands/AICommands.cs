using System;
using System.Net.Http;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Contexts;
using TacoDiscordBot.Services.Interface;
using TacoDiscordBot.Services;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Commands;

public class AICommands : ApplicationCommandModule
{
    [SlashCommand("ai", "AI にメッセージを送信し応答を受け取ります。")]
    // Slash Command の入力をサービス呼び出しへ渡します。
    public async Task Ai(
        InteractionContext ctx,
        [Option("message", "AI に送信するメッセージ")] string message
    )
    {
        // テスト可能なコンテキストへ変換して AI 応答処理を実行します。
        await AiAsync(
            new InteractionResponseContext(ctx),
            message,
            BotHost.AiService,
            ctx.Guild?.Id ?? 0UL,
            ctx.User.Id,
            ctx.User.Username
        );
    }

    public async Task AiAsync(
        IInteractionResponseContext context,
        string message,
        IAiService aiService,
        ulong guildId = 0UL,
        ulong userId = 0UL,
        string userName = null
    )
    {
        // AI サービスの状態を確認し、未設定の場合はエラーを返します。
        if (aiService == null)
        {
            await context.RespondAsync(
                Strings.AiServiceNotSet,
                true
            );

            return;
        }

        // 応答を遅延させてから AI への問い合わせを開始します。
        await context.DeferResponseAsync();

        var combined = AiPrompt.Build(message, userId, userName);

        string reply;

        // ギルド単位の会話履歴を考慮して AI の応答を取得します。
        try
        {
            reply = guildId == 0UL
                ? await aiService.SendToGeminiAsync(combined)
                : await aiService.SendToGeminiAsync(guildId, combined);
        }
        catch (InvalidOperationException ex)
        {
            Logger.Error(ex, "AICommands.Ai: InvalidOperationException");

            await context.EditResponseAsync(
                Strings.GeminiApiKeyNotSet
            );

            return;
        }
        catch (UnauthorizedAccessException)
        {
                await context.EditResponseAsync(Strings.GeminiApiKeyInvalid);

            return;
        }
        catch (HttpRequestException ex)
        {
            var content = ex.Message.Contains("429")
                ? Strings.GeminiRateLimited
                : (int?)ex.StatusCode == 500
                    ? Strings.AiResponseUnavailable
                : $"{Strings.GeminiApiErrorPrefix}{ex.Message}";

            await context.EditResponseAsync(content);

            return;
        }

        // 空の応答をユーザー向けの既定メッセージへ置き換えます。
        if (string.IsNullOrWhiteSpace(reply))
            reply = Strings.AiEmptyResponse;

        await context.EditResponseAsync(reply);
    }

}
