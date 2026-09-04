using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TacoDiscordBot.Services;
using Xunit;

namespace TacoDiscordBot.Tests.CommandTest;

public class AIServiceTests
{
    // Gemini の候補および出力形式から本文を取得できることを検証します。
    // API キー未設定時に適切な例外となることを検証します。
    // Forbidden 応答を認証エラーとして扱うことを検証します。
    [Fact]
    public async Task candidates形式の応答本文を取得できる()
    {
        var previousKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        var previousEndpoint = Environment.GetEnvironmentVariable("GEMINI_API_ENDPOINT");

        try
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", "test-key");
            Environment.SetEnvironmentVariable(
                "GEMINI_API_ENDPOINT",
                "https://example.test/gemini"
            );

            var handler = new StubHttpMessageHandler(
                HttpStatusCode.OK,
                "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"mock reply\"}]}}]}"
            );
            var service = new AIService(
                null,
                null,
                new HttpClient(handler)
            );

            var result = await service.SendToGeminiAsync("hello");

            Assert.Equal("mock reply", result);
            Assert.NotNull(handler.Request);
            Assert.Equal(HttpMethod.Post, handler.Request.Method);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", previousKey);
            Environment.SetEnvironmentVariable("GEMINI_API_ENDPOINT", previousEndpoint);
        }
    }

    [Fact]
    public async Task サーバーエラーではレスポンス本文を例外メッセージに含めない()
    {
        var previousKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        var previousEndpoint = Environment.GetEnvironmentVariable("GEMINI_API_ENDPOINT");

        try
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", "test-key");
            Environment.SetEnvironmentVariable("GEMINI_API_ENDPOINT", "https://example.test/gemini");

            var service = new AIService(
                null,
                null,
                new HttpClient(new StubHttpMessageHandler(
                    HttpStatusCode.InternalServerError,
                    "{\"error\":{\"message\":\"internal details\"}}"
                ))
            );

            var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
                service.SendToGeminiAsync("hello"));

            Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
            Assert.DoesNotContain("internal details", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", previousKey);
            Environment.SetEnvironmentVariable("GEMINI_API_ENDPOINT", previousEndpoint);
        }
    }

    [Fact]
    public async Task レート制限応答ではHttpRequestExceptionを発生させる()
    {
        var previousKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        var previousEndpoint = Environment.GetEnvironmentVariable("GEMINI_API_ENDPOINT");

        try
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", "test-key");
            Environment.SetEnvironmentVariable("GEMINI_API_ENDPOINT", "https://example.test/gemini");

            var service = new AIService(
                null,
                null,
                new HttpClient(new StubHttpMessageHandler((HttpStatusCode)429, "rate limited"))
            );

            var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
                service.SendToGeminiAsync("hello"));

            Assert.Contains("429", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", previousKey);
            Environment.SetEnvironmentVariable("GEMINI_API_ENDPOINT", previousEndpoint);
        }
    }

    [Fact]
    public async Task 応答本文が存在しない場合は空文字列を返す()
    {
        var previousKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        var previousEndpoint = Environment.GetEnvironmentVariable("GEMINI_API_ENDPOINT");

        try
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", "test-key");
            Environment.SetEnvironmentVariable("GEMINI_API_ENDPOINT", "https://example.test/gemini");

            var service = new AIService(
                null,
                null,
                new HttpClient(new StubHttpMessageHandler(HttpStatusCode.OK, "{}"))
            );

            var result = await service.SendToGeminiAsync("hello");

            Assert.Null(result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", previousKey);
            Environment.SetEnvironmentVariable("GEMINI_API_ENDPOINT", previousEndpoint);
        }
    }

    [Fact]
    public async Task APIキー未設定ではInvalidOperationExceptionを発生させる()
    {
        var previousKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        try
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);

            var service = new AIService(null, null, new HttpClient(new StubHttpMessageHandler(
                HttpStatusCode.OK,
                "{}"
            )));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SendToGeminiAsync("hello")
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", previousKey);
        }
    }

    [Fact]
    public async Task output形式の応答本文を取得できる()
    {
        var previousKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        var previousEndpoint = Environment.GetEnvironmentVariable("GEMINI_API_ENDPOINT");

        try
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", "test-key");
            Environment.SetEnvironmentVariable("GEMINI_API_ENDPOINT", "https://example.test/gemini");

            var service = new AIService(null, null, new HttpClient(new StubHttpMessageHandler(
                HttpStatusCode.OK,
                "{\"output\":[{\"content\":[\"one\",\"two\"]}]}"
            )));

            var result = await service.SendToGeminiAsync("hello");

            Assert.Equal("onetwo", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", previousKey);
            Environment.SetEnvironmentVariable("GEMINI_API_ENDPOINT", previousEndpoint);
        }
    }

    [Fact]
    public async Task Forbidden応答では認証例外を発生させる()
    {
        var previousKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        var previousEndpoint = Environment.GetEnvironmentVariable("GEMINI_API_ENDPOINT");

        try
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", "test-key");
            Environment.SetEnvironmentVariable(
                "GEMINI_API_ENDPOINT",
                "https://example.test/gemini"
            );

            var service = new AIService(
                null,
                null,
                new HttpClient(new StubHttpMessageHandler(HttpStatusCode.Forbidden, "forbidden"))
            );

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.SendToGeminiAsync("hello")
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", previousKey);
            Environment.SetEnvironmentVariable("GEMINI_API_ENDPOINT", previousEndpoint);
        }
    }

    // HTTP 呼び出しを固定応答へ差し替えるテスト用ハンドラーです。
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public HttpRequestMessage Request { get; private set; }

        public StubHttpMessageHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Request = request;

            return Task.FromResult(
                new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent(_body),
                }
            );
        }
    }
}