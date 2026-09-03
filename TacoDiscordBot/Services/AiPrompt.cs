namespace TacoDiscordBot.Services;

public static class AiPrompt
{
    private const string SystemPrompt = "あなたは Discord 上で動作する AI Bot です。\n"
        + "メッセージは Discord に適した長さで、読みやすく簡潔に出力します。\n"
        + "ユーザーの質問には少し毒舌でフレンドリーに答えます。\n"
        + "過度に長文にせず、必要な情報をまとめて返します。";

    public static string Build(string message)
    {
        // 共通のシステム指示とユーザー入力を AI 用のプロンプトにまとめます。
        return SystemPrompt + "\n\n" + message;
    }
}
