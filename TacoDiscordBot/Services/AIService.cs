using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using TacoDiscordBot.Repository;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Services;

public class AIService
{
    private readonly DiscordClient _client;
    private readonly AiChannelService _channelService;
    private readonly HttpClient _http = new();

    public AIService(DiscordClient client, AiChannelService channelService)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));

        _channelService = channelService;
    }

    public async Task HandleMessageCreated(DiscordClient sender, MessageCreateEventArgs e)
    {
        // Bot や対象外チャンネルのメッセージを除外し、対象メッセージだけ AI へ渡します。
        var msg = e.Message;

        Logger.Info(
            "AIService: メッセージ受信 guild={GuildId} channel={ChannelId} author={AuthorId}",
            e.Guild?.Id,
            msg?.Channel?.Id,
            msg?.Author?.Id
        );

        if (msg == null)
            return;
        if (msg.Author == null)
            return;
        if (msg.Author.IsBot)
            return;
        if (msg.WebhookId != null)
            return;

        var guildId = e.Guild?.Id ?? 0UL;

        if (guildId == 0)
            return;

        if (_channelService == null || !_channelService.IsTargetChannel(guildId, msg.Channel.Id))
        {
            return;
        }

        var input = msg.Content ?? string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            Logger.Info("AIService: メッセージ本文が空のため処理を終了");
            return;
        }

        await msg.Channel.TriggerTypingAsync();

        string reply;

        try
        {
            reply = await SendToGeminiAsync(input);
        }
        catch (InvalidOperationException ex)
        {
            Logger.Error(ex, "AIService: API キー未設定");
            await msg.Channel.SendMessageAsync(
                "Gemini API キーが設定されていません。環境変数 GEMINI_API_KEY を設定してください。"
            );

            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Error(ex, "AIService: Gemini API の認証に失敗");
            await msg.Channel.SendMessageAsync("Gemini API キーが無効または権限がありません。");

            return;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error(ex, "AIService: Gemini API 呼び出しに失敗");
            if (ex.Message.Contains("429"))
            {
                await msg.Channel.SendMessageAsync(
                    "Gemini API がレート制限されました。しばらくしてから再度お試しください。"
                );
            }
            else
            {
                await msg.Channel.SendMessageAsync($"Gemini API エラー: {ex.Message}");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(reply))
        {
            await msg.Channel.SendMessageAsync(
                "AI 応答を取得できませんでした。後でもう一度試してください。"
            );

            return;
        }

        await msg.Channel.SendMessageAsync(reply);
    }

    public async Task<string> SendToGeminiAsync(string prompt)
    {
        // 環境変数から API 設定を取得し、Gemini の応答本文を抽出します。
        var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        Logger.Info(
            "SendToGeminiAsync: GEMINI_API_KEY present={ApiKeyPresent} length={ApiKeyLength}",
            !string.IsNullOrWhiteSpace(key),
            key?.Length ?? 0
        );

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "Gemini API key is not configured. " + "Set GEMINI_API_KEY environment variable."
            );
        }

        var model = Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-3.1-flash-lite";

        var endpoint = Environment.GetEnvironmentVariable("GEMINI_API_ENDPOINT");

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint =
                $"https://generativelanguage.googleapis.com/"
                + $"v1beta/models/{model}:generateContent"
                + $"?key={Uri.EscapeDataString(key)}";
        }

        var reqObj = new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new { temperature = 0.7, maxOutputTokens = 1000 },
        };

        var json = JsonSerializer.Serialize(reqObj);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);

        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(request);
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            if ((int)resp.StatusCode == 429)
            {
                throw new HttpRequestException("Rate limited by Gemini API (429)");
            }

            if ((int)resp.StatusCode == 401 || (int)resp.StatusCode == 403)
            {
                throw new UnauthorizedAccessException(
                    "Gemini API key is invalid or not authorized"
                );
            }

            throw new HttpRequestException($"Gemini API returned {(int)resp.StatusCode}: {body}");
        }

        using var doc = JsonDocument.Parse(body);

        if (
            doc.RootElement.TryGetProperty("candidates", out var candidates)
            && candidates.ValueKind == JsonValueKind.Array
            && candidates.GetArrayLength() > 0
        )
        {
            var first = candidates[0];

            if (first.TryGetProperty("content", out var content))
            {
                if (content.ValueKind == JsonValueKind.Object)
                {
                    if (
                        content.TryGetProperty("parts", out var parts)
                        && parts.ValueKind == JsonValueKind.Array
                        && parts.GetArrayLength() > 0
                    )
                    {
                        var part = parts[0];

                        if (
                            part.ValueKind == JsonValueKind.Object
                            && part.TryGetProperty("text", out var textElement)
                            && textElement.ValueKind == JsonValueKind.String
                        )
                        {
                            return textElement.GetString();
                        }
                    }

                    if (
                        content.TryGetProperty("text", out var directText)
                        && directText.ValueKind == JsonValueKind.String
                    )
                    {
                        return directText.GetString();
                    }
                }
                else if (content.ValueKind == JsonValueKind.String)
                {
                    return content.GetString();
                }
            }
        }

        if (
            doc.RootElement.TryGetProperty("output", out var output)
            && output.ValueKind == JsonValueKind.Array
            && output.GetArrayLength() > 0
        )
        {
            var first = output[0];

            if (first.TryGetProperty("content", out var outputContent))
            {
                if (
                    outputContent.ValueKind == JsonValueKind.Array
                    && outputContent.GetArrayLength() > 0
                )
                {
                    var sb = new StringBuilder();

                    foreach (var item in outputContent.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            sb.Append(item.GetString());
                        }
                    }

                    return sb.ToString();
                }

                if (outputContent.ValueKind == JsonValueKind.String)
                {
                    return outputContent.GetString();
                }
            }
        }

        return body;
    }
}
