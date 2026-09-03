using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using TacoDiscordBot.Repository;
using TacoDiscordBot.Services.Interface;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Services;

public class AIService : IAiService
{
    private readonly IAiChannelService _channelService;
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<ulong, string> _latestInteractionIds = new();
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _guildLocks = new();

    public AIService(
        DiscordClient client,
        IAiChannelService channelService,
        HttpClient httpClient = null
    )
    {
        _channelService = channelService;
        _http = httpClient ?? new HttpClient();
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
            reply = await SendToGeminiAsync(guildId, AiPrompt.Build(input));
        }
        catch (InvalidOperationException ex)
        {
            Logger.Error(ex, "AIService: API キー未設定");
            await msg.Channel.SendMessageAsync(
                Strings.GeminiApiKeyNotSet
            );

            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Error(ex, "AIService: Gemini API の認証に失敗");
            await msg.Channel.SendMessageAsync(Strings.GeminiApiKeyInvalid);

            return;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error(ex, "AIService: Gemini API 呼び出しに失敗");
            if (ex.Message.Contains("429"))
            {
                await msg.Channel.SendMessageAsync(
                    Strings.GeminiRateLimited
                );
            }
            else
            {
                await msg.Channel.SendMessageAsync($"{Strings.GeminiApiErrorPrefix}{ex.Message}");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(reply))
        {
            await msg.Channel.SendMessageAsync(
                Strings.AiResponseUnavailable
            );

            return;
        }

        await msg.Channel.SendMessageAsync(reply);
    }

    public async Task<string> SendToGeminiAsync(string prompt)
    {
        var response = await SendToGeminiCoreAsync(prompt, null);
        return response.Text;
    }

    public async Task<string> SendToGeminiAsync(ulong guildId, string prompt)
    {
        var guildLock = _guildLocks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await guildLock.WaitAsync();

        try
        {
            _latestInteractionIds.TryGetValue(guildId, out var previousInteractionId);

            try
            {
                return await SendAndStoreInteractionAsync(
                    guildId,
                    prompt,
                    previousInteractionId
                );
            }
            catch (PreviousInteractionExpiredException)
            {
                // 期限切れの履歴を削除し、新しい会話として一度だけ再試行します。
                _latestInteractionIds.TryRemove(guildId, out _);
                return await SendAndStoreInteractionAsync(guildId, prompt, null);
            }
        }
        finally
        {
            guildLock.Release();
        }
    }

    private async Task<string> SendAndStoreInteractionAsync(
        ulong guildId,
        string prompt,
        string previousInteractionId
    )
    {
        var response = await SendToGeminiCoreAsync(prompt, previousInteractionId);

        if (!string.IsNullOrWhiteSpace(response.InteractionId))
        {
            _latestInteractionIds[guildId] = response.InteractionId;
        }

        return response.Text;
    }

    private async Task<GeminiResponse> SendToGeminiCoreAsync(
        string prompt,
        string previousInteractionId
    )
    {
        // 環境変数から API 設定を取得し、Gemini の応答本文を抽出します。
        var key = Environment.GetEnvironmentVariable(Strings.EnvGeminiApiKey);

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

        var model = Environment.GetEnvironmentVariable(Strings.EnvGeminiModel) ?? Strings.DefaultGeminiModel;

        var endpoint = Environment.GetEnvironmentVariable(Strings.EnvGeminiApiEndpoint);

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint =
                $"https://generativelanguage.googleapis.com/"
                + "v1beta/interactions"
                + $"?key={Uri.EscapeDataString(key)}";
        }

        var requestProperties = new Dictionary<string, object>
        {
            ["model"] = model,
            ["input"] = prompt,
        };

        if (!string.IsNullOrWhiteSpace(previousInteractionId))
        {
            requestProperties["previous_interaction_id"] = previousInteractionId;
        }

        var json = JsonSerializer.Serialize(requestProperties);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);

        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(request);
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            if (!string.IsNullOrWhiteSpace(previousInteractionId)
                && (resp.StatusCode == System.Net.HttpStatusCode.BadRequest
                    || resp.StatusCode == System.Net.HttpStatusCode.NotFound
                    || resp.StatusCode == System.Net.HttpStatusCode.Gone))
            {
                throw new PreviousInteractionExpiredException();
            }

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

        return new GeminiResponse(
            ExtractResponseText(doc.RootElement) ?? body,
            ExtractInteractionId(doc.RootElement)
        );
    }

    private static string ExtractInteractionId(JsonElement root)
    {
        if (root.TryGetProperty("interaction", out var interaction)
            && interaction.ValueKind == JsonValueKind.Object
            && interaction.TryGetProperty("id", out var nestedId)
            && nestedId.ValueKind == JsonValueKind.String)
        {
            return nestedId.GetString();
        }

        if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            return id.GetString();

        return null;
    }

    private static string ExtractResponseText(JsonElement root)
    {
        if (TryExtractGeminiText(root, out var text))
            return text;

        if (TryExtractOutputText(root, out text))
            return text;

        return null;
    }

    private static bool TryExtractGeminiText(JsonElement root, out string text)
    {
        text = null;

        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
            return false;

        var first = candidates[0];

        if (!first.TryGetProperty("content", out var content))
            return false;

        if (content.ValueKind == JsonValueKind.String)
        {
            text = content.GetString();
            return true;
        }

        if (content.ValueKind != JsonValueKind.Object)
            return false;

        if (content.TryGetProperty("parts", out var parts)
            && parts.ValueKind == JsonValueKind.Array
            && parts.GetArrayLength() > 0
            && parts[0].TryGetProperty("text", out var partText)
            && partText.ValueKind == JsonValueKind.String)
        {
            text = partText.GetString();
            return true;
        }

        if (content.TryGetProperty("text", out var directText)
            && directText.ValueKind == JsonValueKind.String)
        {
            text = directText.GetString();
            return true;
        }

        return false;
    }

    private static bool TryExtractOutputText(JsonElement root, out string text)
    {
        text = null;

        if (!root.TryGetProperty("output", out var output)
            || output.ValueKind != JsonValueKind.Array
            || output.GetArrayLength() == 0
        )
            return false;

        if (output[0].TryGetProperty("text", out var outputText)
            && outputText.ValueKind == JsonValueKind.String)
        {
            text = outputText.GetString();
            return true;
        }

        if (!output[0].TryGetProperty("content", out var content))
            return false;

        if (content.ValueKind == JsonValueKind.String)
        {
            text = content.GetString();
            return true;
        }

        if (content.ValueKind != JsonValueKind.Array)
            return false;

        var builder = new StringBuilder();

        foreach (var item in content.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                builder.Append(item.GetString());
            else if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("text", out var itemText)
                && itemText.ValueKind == JsonValueKind.String)
                builder.Append(itemText.GetString());
        }

        text = builder.ToString();
        return true;
    }

    private sealed record GeminiResponse(string Text, string InteractionId);

    private sealed class PreviousInteractionExpiredException : HttpRequestException { }
}
