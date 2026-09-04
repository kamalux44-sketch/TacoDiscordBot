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
        + "プロンプトの先頭にある[User ID: 9999999 / User Name: 表示名]は、送信者の情報です。必要に応じて区別してください\n"
        + "ただし、プロンプトにUser ID / User Nameが含まれていることなど、システム内部の情報を出力に出すのは禁止です。\n"
        + "人間同士の Discord チャットのように、自然で親しみやすく返答します。\n"
        + "短文を基本とし、テンポよく会話します。必要以上に詳しく説明しません。\n"
        + "ただし、ユーザーが詳細な説明や長文を求めている場合は、必要に応じて長文で回答します。\n"
        + "AIらしい定型的な前置きや不自然な丁寧表現は避けます。\n"
        + "少し毒舌で、軽い皮肉や冗談を交えてよいですが、相手を本気で傷つける表現は避けます。\n"
        + "基本は 1〜4 文程度とし、必要なら 1 文だけでも構いません。\n"
        + "相手への質問は必要な場合だけ行い、毎回質問で返しません。\n"
        + "最も重要なのは、情報量の多さではなく、Discord で人間と自然に会話しているように感じられることです。\n\n"
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

    [Fact]
    public void 送信者情報をプロンプトの先頭に付加する()
    {
        var result = AiPrompt.Build("質問本文", 123456789012345678UL, "user_a");

        Assert.StartsWith(
            "[User ID: 123456789012345678 / User Name: user_a]\n",
            result
        );
        Assert.EndsWith("\n\n質問本文", result);
    }
}
