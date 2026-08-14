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
    public const string VcLogJoinFmt = "参加: {0} ({1})";
    public const string VcLogLeaveFmt = "退出: {0} ({1})";
    public const string VcLogMoveFmt = "移動: {0} ({1} → {2}) ({3})";
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
        "エラー発生！たこクオリティだから仕方ないね！",
        "エラー発生！気が向いたら修正します！",
        "エラー発生！壊さないで！優しく使ってね！",
        "エラー発生！直せる人募集中！",
        "エラー発生！このbotは壊れてます！叩けば直るかも！"
    };
}
