using System;
using System.Net.Http;
using System.Threading.Tasks;
using Moq;
using TacoDiscordBot.Commands;
using TacoDiscordBot.Contexts;
using TacoDiscordBot.Services.Interface;
using Xunit;

namespace TacoDiscordBot.Tests.CommandTest;

public class AICommandsTests
{
    // AI サービス未設定時のエラー応答を検証します。
    // AI 応答成功時のメッセージ更新を検証します。
    // 空の AI 応答が既定メッセージへ置換されることを検証します。
    // 認証エラー時のユーザー向け応答を検証します。
    // レート制限時のユーザー向け応答を検証します。
    // その他の HTTP エラー時の応答を検証します。

    [Fact]
    public async Task サービス未設定の場合は非公開のエラーを返す()
    {
        var context = CreateContextMock();
        var command = new AICommands();

        await command.AiAsync(context.Object, "質問", null);

        context.Verify(x => x.RespondAsync(
            "AI サービスは未設定です。管理者が GEMINI_API_KEY を設定しているか確認してください。",
            true), Times.Once);
        context.Verify(x => x.DeferResponseAsync(), Times.Never);
    }

    [Fact]
    public async Task Geminiが成功した場合は応答を返す()
    {
        var context = CreateContextMock();
        var ai = new Mock<IAiService>();
        ai.Setup(x => x.SendToGeminiAsync(It.Is<string>(p => p.EndsWith("\n\n質問"))))
            .ReturnsAsync("回答");

        await new AICommands().AiAsync(context.Object, "質問", ai.Object);

        context.Verify(x => x.DeferResponseAsync(), Times.Once);
        context.Verify(x => x.EditResponseAsync("回答"), Times.Once);
    }

    [Fact]
    public async Task AI応答が空の場合は代替メッセージを返す()
    {
        var context = CreateContextMock();
        var ai = new Mock<IAiService>();
        ai.Setup(x => x.SendToGeminiAsync(It.IsAny<string>())).ReturnsAsync(" ");

        await new AICommands().AiAsync(context.Object, "質問", ai.Object);

        context.Verify(x => x.EditResponseAsync("(応答が空でした)"), Times.Once);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException), "Gemini API キーが設定されていません。環境変数 GEMINI_API_KEY を設定してください。")]
    [InlineData(typeof(UnauthorizedAccessException), "Gemini API キーが無効または権限がありません。")]
    public async Task 認証エラーの場合は安全なエラーメッセージを返す(
        Type exceptionType,
        string expectedMessage
    )
    {
        var context = CreateContextMock();
        var ai = new Mock<IAiService>();
        ai.Setup(x => x.SendToGeminiAsync(It.IsAny<string>()))
            .ThrowsAsync((Exception)Activator.CreateInstance(exceptionType));

        await new AICommands().AiAsync(context.Object, "質問", ai.Object);

        context.Verify(x => x.EditResponseAsync(expectedMessage), Times.Once);
    }

    [Fact]
    public async Task レート制限の場合は専用メッセージを返す()
    {
        var context = CreateContextMock();
        var ai = new Mock<IAiService>();
        ai.Setup(x => x.SendToGeminiAsync(It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("response status 429"));

        await new AICommands().AiAsync(context.Object, "質問", ai.Object);

        context.Verify(x => x.EditResponseAsync(
            "Gemini API がレート制限されました。しばらくしてから再度お試しください。"), Times.Once);
    }

    [Fact]
    public async Task 予期しないHTTPエラーの場合はエラーメッセージを返す()
    {
        var context = CreateContextMock();
        var ai = new Mock<IAiService>();
        ai.Setup(x => x.SendToGeminiAsync(It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("server error"));

        await new AICommands().AiAsync(context.Object, "質問", ai.Object);

        context.Verify(x => x.EditResponseAsync("Gemini API エラー: server error"), Times.Once);
    }

    private static Mock<IInteractionResponseContext> CreateContextMock()
    {
        var context = new Mock<IInteractionResponseContext>();
        context.Setup(x => x.RespondAsync(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);
        context.Setup(x => x.DeferResponseAsync()).Returns(Task.CompletedTask);
        context.Setup(x => x.EditResponseAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        return context;
    }
}
