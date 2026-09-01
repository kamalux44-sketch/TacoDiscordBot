using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
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
        _channelService = channelService; // may be null if not configured
    }

    public async Task HandleMessageCreated(DiscordClient sender, MessageCreateEventArgs e)
    {
        try
        {
            var msg = e.Message;
            if (msg == null) return;
            if (msg.Author == null) return;
            if (msg.Author.IsBot) return;
            if (msg.WebhookId != null) return;

            var gid = e.Guild?.Id ?? 0UL;
            if (gid == 0) return;
            if (_channelService == null || !_channelService.IsTargetChannel(gid, msg.Channel.Id)) return;

            // Send typing indicator
            try { await msg.Channel.TriggerTypingAsync(); } catch { }

            var input = msg.Content ?? string.Empty;
            if (string.IsNullOrWhiteSpace(input)) return;

            var reply = await SendToGeminiAsync(input);
            if (reply == null)
            {
                await msg.Channel.SendMessageAsync("AI 応答を取得できませんでした。後でもう一度試してください。");
                return;
            }

            await msg.Channel.SendMessageAsync(reply);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "AIService.HandleMessageCreated");
        }
    }

    public async Task<string> SendToGeminiAsync(string prompt)
    {
        // Read API key from environment variable
        string key = null;
        try
        {
            key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            Logger.Info($"SendToGeminiAsync: GEMINI_API_KEY present: {!string.IsNullOrWhiteSpace(key)} length={(key?.Length ?? 0)}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "SendToGeminiAsync: 環境変数チェック失敗");
        }

        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Gemini API key is not configured. Set GEMINI_API_KEY environment variable.");

        // Basic implementation targeting Google Generative Language endpoint for Gemini models.
        // Use API key in query string with the v1beta generateContent endpoint by default.
        // The exact request/response schema may differ; we attempt a best-effort approach and
        // fall back to returning raw response text if structured parsing fails.
        var model = Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-3.1-flash-lite";
        var endpoint = Environment.GetEnvironmentVariable("GEMINI_API_ENDPOINT");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(key)}";
        }

        // リクエストペイロードを指定の構造にする
        // {
        //   "contents": [ { "parts": [ { "text": "..." } ] } ],
        //   "generationConfig": { "temperature": 0.7, "maxOutputTokens": 1000 }
        // }
        var reqObj = new
        {
            contents = new[] {
                new {
                    parts = new[] { new { text = prompt } }
                }
            },
            generationConfig = new {
                temperature = 0.7,
                maxOutputTokens = 1000
            }
        };

        var json = JsonSerializer.Serialize(reqObj);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var resp = await _http.SendAsync(request);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                if ((int)resp.StatusCode == 429)
                    throw new HttpRequestException("Rate limited by Gemini API (429)");
                if ((int)resp.StatusCode == 401 || (int)resp.StatusCode == 403)
                    throw new UnauthorizedAccessException("Gemini API key is invalid or not authorized");

                throw new HttpRequestException($"Gemini API returned {(int)resp.StatusCode}: {body}");
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                // try common shapes: candidates[0].content, output[0].content
                if (doc.RootElement.TryGetProperty("candidates", out var cand) && cand.ValueKind == JsonValueKind.Array && cand.GetArrayLength() > 0)
                {
                    var first = cand[0];
                    if (first.TryGetProperty("content", out var content))
                    {
                        // content is typically an object containing parts array
                        if (content.ValueKind == JsonValueKind.Object)
                        {
                            if (content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array && parts.GetArrayLength() > 0)
                            {
                                var part = parts[0];
                                if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                                {
                                    return textEl.GetString();
                                }
                            }
                            // fallback: maybe content has a text property directly
                            if (content.TryGetProperty("text", out var directText) && directText.ValueKind == JsonValueKind.String)
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

                if (doc.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array && output.GetArrayLength() > 0)
                {
                    var first = output[0];
                    if (first.TryGetProperty("content", out var content2))
                    {
                        // content may be array
                        if (content2.ValueKind == JsonValueKind.Array && content2.GetArrayLength() > 0)
                        {
                            var sb = new StringBuilder();
                            foreach (var it in content2.EnumerateArray())
                            {
                                if (it.ValueKind == JsonValueKind.String) sb.Append(it.GetString());
                            }
                            return sb.ToString();
                        }
                        else if (content2.ValueKind == JsonValueKind.String)
                        {
                            return content2.GetString();
                        }
                    }
                }

                // fallback: return whole body
                return body;
            }
            catch (JsonException)
            {
                return body;
            }
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            throw;
        }
    }
}
