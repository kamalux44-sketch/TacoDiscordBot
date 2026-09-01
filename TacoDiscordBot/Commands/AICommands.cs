using System;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.SlashCommands;
using DSharpPlus.Entities;
using TacoDiscordBot.Services;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Commands;

public class AICommands : ApplicationCommandModule
{
    [SlashCommand("ai", "AI にメッセージを送信し応答を受け取ります。")]
    public async Task Ai(InteractionContext ctx, [Option("message", "AI に送信するメッセージ")] string message)
    {
        try
        {
            if (BotHost.AiService == null)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent("AI サービスは未設定です。管理者が GEMINI_API_KEY を設定しているか確認してください。").AsEphemeral(true));
                return;
            }

            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);

            string reply;
            try
            {
                // コマンドからの呼び出しには指定の設定プロンプトを先頭に追加する
                var systemPrompt = string.Join("\n", new[] {
                    "あなたは Discord 上で動作する AI Bot です。",
                    "メッセージは Discord に適した長さで、読みやすく簡潔に出力します。",
                    "ユーザーの質問には少し毒舌でフレンドリーに答えます。",
                    "過度に長文にせず、必要な情報をまとめて返します。"
                });
                var combined = systemPrompt + "\n\n" + message;
                reply = await BotHost.AiService.SendToGeminiAsync(combined);
            }
            catch (InvalidOperationException ex)
            {
                Logger.Error(ex, "AICommands.Ai: InvalidOperationException");
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Gemini API キーが設定されていません。環境変数 GEMINI_API_KEY を設定してください。"));
                return;
            }
            catch (UnauthorizedAccessException)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Gemini API キーが無効または権限がありません。"));
                return;
            }
            catch (HttpRequestException ex)
            {
                if (ex.Message.Contains("429"))
                    await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Gemini API がレート制限されました。しばらくしてから再度お試しください。"));
                else
                    await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"Gemini API エラー: {ex.Message}"));
                return;
            }

            if (string.IsNullOrWhiteSpace(reply)) reply = "(応答が空でした)";

            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(reply));
        }
        catch (Exception ex)
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("AI 処理中にエラーが発生しました。"));
            Logger.Error(ex, "AICommands.Ai");
        }
    }

    [SlashCommand("aichannel", "このチャンネルを AI 会話チャンネルとして設定します（管理用）")]
    public async Task AiChannel(InteractionContext ctx)
    {
        var guildId = ctx.Guild?.Id ?? 0UL;
        if (guildId == 0)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent(Strings.CommandGuildOnly).AsEphemeral(true));
            return;
        }

        if (BotHost.AiChannelService == null)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent("AI サービスは未設定です。管理者が DB と GEMINI_API_KEY を設定しているか確認してください。"));
            return;
        }

        await BotHost.AiChannelService.SetChannelAsync(guildId, ctx.Channel.Id);
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent($"このチャンネルを AI 会話チャンネルとして設定しました。 (# {ctx.Channel.Name})").AsEphemeral(true));
    }
}
