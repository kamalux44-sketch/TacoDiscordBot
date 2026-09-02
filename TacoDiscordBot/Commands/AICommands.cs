using System;
using System.Net.Http;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Services;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Commands;

public class AICommands : ApplicationCommandModule
{
    [SlashCommand("ai", "AI にメッセージを送信し応答を受け取ります。")]
    public async Task Ai(
        InteractionContext ctx,
        [Option("message", "AI に送信するメッセージ")] string message
    )
    {
        if (BotHost.AiService == null)
        {
            await ctx.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(
                        "AI サービスは未設定です。管理者が GEMINI_API_KEY を設定しているか確認してください。"
                    )
                    .AsEphemeral(true)
            );

            return;
        }

        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);

        var systemPrompt = string.Join(
            "\n",
            new[]
            {
                "あなたは Discord 上で動作する AI Bot です。",
                "メッセージは Discord に適した長さで、読みやすく簡潔に出力します。",
                "ユーザーの質問には少し毒舌でフレンドリーに答えます。",
                "過度に長文にせず、必要な情報をまとめて返します。",
            }
        );

        var combined = systemPrompt + "\n\n" + message;

        string reply;

        try
        {
            reply = await BotHost.AiService.SendToGeminiAsync(combined);
        }
        catch (InvalidOperationException ex)
        {
            Logger.Error(ex, "AICommands.Ai: InvalidOperationException");

            await ctx.EditResponseAsync(
                new DiscordWebhookBuilder().WithContent(
                    "Gemini API キーが設定されていません。環境変数 GEMINI_API_KEY を設定してください。"
                )
            );

            return;
        }
        catch (UnauthorizedAccessException)
        {
            await ctx.EditResponseAsync(
                new DiscordWebhookBuilder().WithContent(
                    "Gemini API キーが無効または権限がありません。"
                )
            );

            return;
        }
        catch (HttpRequestException ex)
        {
            var content = ex.Message.Contains("429")
                ? "Gemini API がレート制限されました。しばらくしてから再度お試しください。"
                : $"Gemini API エラー: {ex.Message}";

            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(content));

            return;
        }

        if (string.IsNullOrWhiteSpace(reply))
            reply = "(応答が空でした)";

        await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(reply));
    }

}
