using System;
using System.Collections.Concurrent;
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
    private readonly AiTalkRepository _repo;
    private readonly ConcurrentDictionary<ulong, ulong> _targets = new();
    private readonly HttpClient _http = new();

    public AIService(DiscordClient client, AiTalkRepository repo = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _repo = repo ?? CreateFromEnvOrNull();

        if (_repo != null)
        {
            try
            {
                var all = _repo.LoadAllAsync().GetAwaiter().GetResult();
                foreach (var kv in all) _targets[kv.Key] = kv.Value;
            }
            catch
            {
                // ignore
            }
        }
    }

    private static AiTalkRepository CreateFromEnvOrNull()
    {
        try
        {
            var host = Environment.GetEnvironmentVariable("PGHOST");
            if (string.IsNullOrWhiteSpace(host))
                return null;

            var port = Environment.GetEnvironmentVariable("PGPORT") ?? Strings.DefaultDBPPort;
            var db = Environment.GetEnvironmentVariable("PGDATABASE") ?? Strings.DefaultDBName;
            var user = Environment.GetEnvironmentVariable("PGUSER");
            var pass = Environment.GetEnvironmentVariable("PGPASSWORD");
            var ssl = Environment.GetEnvironmentVariable("PGSSLMODE");

            var parts = new List<string>
            {
                $"Host={host}",
                $"Port={port}",
                $"Database={db}"
            };
            if (!string.IsNullOrWhiteSpace(user)) parts.Add($"Username={user}");
            if (!string.IsNullOrWhiteSpace(pass)) parts.Add($"Password={pass}");
            if (!string.IsNullOrWhiteSpace(ssl)) parts.Add($"SslMode={ssl}");

            var conn = string.Join(";", parts);
            var baseRepo = new Repository.BaseRepository(conn, s => Console.WriteLine($"[DB] {s}"));
            if (!baseRepo.IsProviderAvailable()) return null;
            return new AiTalkRepository(baseRepo);
        }
        catch
        {
            return null;
        }
    }

    public bool IsConfigured => _repo != null || _targets.Count > 0;

    public bool IsTargetChannel(ulong guildId, ulong channelId)
    {
        if (_repo != null) return _targets.TryGetValue(guildId, out var v) && v == channelId;
        return false;
    }

    public async Task SetChannelAsync(ulong guildId, ulong channelId)
    {
        if (_repo != null)
        {
            await _repo.SetTargetAsync(guildId, channelId);
            _targets[guildId] = channelId;
        }
        else
        {
            _targets[guildId] = channelId; // in-memory fallback
        }
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
            if (!IsTargetChannel(gid, msg.Channel.Id)) return;

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
        var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Gemini API key is not configured. Set GEMINI_API_KEY environment variable.");

        // Basic implementation targeting Google Generative Language endpoint for Gemini models.
        // The exact request/response schema may differ; we attempt a best-effort approach and
        // fall back to returning raw response text if structured parsing fails.
        var endpoint = Environment.GetEnvironmentVariable("GEMINI_API_ENDPOINT") ?? "https://generativelanguage.googleapis.com/v1/models/gemini-3.1-flash-lite:generate";

        var reqObj = new
        {
            prompt = new { text = prompt },
            temperature = 0.2,
            maxOutputTokens = 512
        };

        var json = JsonSerializer.Serialize(reqObj);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
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
                        return content.GetString();
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
