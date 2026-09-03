using TacoDiscordBot.Services;
using Xunit;

namespace TacoDiscordBot.Tests;

public class AiPromptTests
{
    // ユーザー入力へ共通システムプロンプトが付加されることを検証します。
    [Fact]
    public void 通常メッセージに共通システムプロンプトを付加する()
    {
        var result = AiPrompt.Build("質問本文");

        Assert.Equal(
            "あなたは Discord 上で動作する AI Bot です。\n"
                + "メッセージは Discord に適した長さで、読みやすく簡潔に出力します。\n"
                + "ユーザーの質問には少し毒舌でフレンドリーに答えます。\n"
                + "過度に長文にせず、必要な情報をまとめて返します。\n\n"
                + "質問本文",
            result
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void 空の入力でも共通システムプロンプトを保持する(string message)
    {
        var result = AiPrompt.Build(message);

        Assert.EndsWith("\n\n" + message, result);
    }

    [Fact]
    public void null入力はnullを文字列化せずプロンプトを構成する()
    {
        var result = AiPrompt.Build(null);

        Assert.EndsWith("\n\n", result);
        Assert.DoesNotContain("null", result);
    }
}
