using System;

namespace TacoDiscordBot;

/// <summary>
/// アプリ内の定数文字列をまとめたクラスです。
/// メッセージに日本語を使うため、ソースは UTF-8 で保存してください。
/// </summary>
public static class Strings
{
    // --- General ---
    public const string EnvTokenMissing = "Environment variable DISCORD_TOKEN is not set. Set it to your bot token.";

    // --- Formatting ---
    public const string DateTimeFormat = "yyyy/MM/dd HH:mm";

    // --- VC log ---
    // 誰が、どのチャンネルで、いつ 入室/退室/移動 したか分かるようにします。
    public const string VcLogJoinFmt = "参加: {0} が {1} に入室 ({2})"; // {0}=user, {1}=channel, {2}=time
    public const string VcLogLeaveFmt = "退出: {0} が {1} から退室 ({2})"; // {0}=user, {1}=channel, {2}=time
    public const string VcLogMoveFmt = "移動: {0} が {1} → {2} ({3})"; // {0}=user, {1}=from, {2}=to, {3}=time
    // 環境変数: VC ログ送信先チャンネル ID (例: "123456789012345678")
    public const string EnvVcLogChannel = "VCLOG_CHANNEL";

    // --- BO (募集) ---
    public const string EmbedTitle = "募集";
    public const string EmbedStartContent = "@here\n募集を開始しました！";
    public const string EmbedUpdatedContent = "@here\n募集を更新しました！";
    public const string ButtonJoin = "参加";
    public const string ButtonCancel = "キャンセル";

    public const string LabelContent = "募集内容";
    public const string LabelOwner = "募集主";
    public const string LabelRank = "ランク";
    public const string LabelParticipants = "参加者";

    public const string VcToggleOn = "指定チャンネルの VC ログを有効化しました。";
    public const string VcToggleOff = "指定チャンネルの VC ログを無効化しました。";
    public const string VcChannelSet = "VC ログ送信先チャンネルを設定しました。";

    public static readonly string[] ErrorMessages = new[]
    {
        "エラー発生！気が向いたら修正します！",
        "エラー発生！壊さないで！優しく使ってね！",
        "エラー発生！直せる人募集中！",
        "エラー発生！たこが今ちょっとイカれてます！",
        "エラー発生！仕様です！（多分！）",
        "エラー発生！今のは見なかったことにしてください！",
        "エラー発生！原因の調査中にまたエラーです！",
        "エラー発生！何かがおかしいです。何かは分かりません！",
        "エラー発生！今のところ、原因は不明です！",
        "エラー発生！見なかったことにしてもう一度お試しください！",
        "エラー発生！何度やってもダメなら、今日はそういう日です！",
        "エラー発生！正常に動く予定ではありました！",
        "エラー発生！うまくいく気配がありません！",
        "エラー発生！ここから先は未知の領域です！",
        "エラー発生！一度深呼吸してから再挑戦してください！",
        "エラー発生！原因を探しています。見つかるとは限りません！",
        "エラー発生！今の操作、なかったことにできませんか？",
        "エラー発生！システムが一時的にやる気を失いました！",
        "エラー発生！ご不便をおかけします。こちらも困っています！",
        "エラー発生！とりあえず、もう一回やってみましょう！",
        "エラー発生！このbotは壊れてます！叩けば直るかも！"
    };
}

