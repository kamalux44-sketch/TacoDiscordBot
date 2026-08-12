using System;

namespace TacoDiscordBot;

/// <summary>
/// アプリケーション内で使用する固定文字列をまとめたクラスです。
/// ハードコードされた文字列をここに集約することで、翻訳や変更を容易にします。
/// </summary>
public static class Strings
{
    // --- 環境 / ファイル ---
    public const string EnvTokenMissing = "Environment variable DISCORD_TOKEN is not set. Set it to your bot token.";

    // --- 日付時刻フォーマット ---
    public const string DateTimeFormat = "yyyy/MM/dd HH:mm";

    // --- VC ログ ---
    public const string VcLogJoinFmt = "入室：{0}({1})";
    public const string VcLogLeaveFmt = "退室：{0}({1})";
    public const string VcLogMoveFmt = "移動：{0} ({1} → {2})({3})";
    // 環境変数名: VC ログ用のチャンネル設定（単一サーバー運用を想定）。
    // 値はチャンネルID（数値）を直接指定します。例: "123456789012345678"
    public const string EnvVcLogChannel = "VCLOG_CHANNEL";

    // --- BO（募集） ---
    // --- BO（募集） ---
    // BO 関連の SQL は各サービスファイル側に定義します。

    // --- メッセージ文言 ---
    public const string EmbedTitle = "募集";
    public const string EmbedStartContent = "@here\n募集が開始されました！";
    public const string EmbedUpdatedContent = "@here\n募集が更新されました！";
    public const string ButtonJoin = "参加";
    public const string ButtonCancel = "参加取消";

    public const string LabelGame = "ゲーム：";
    public const string LabelOwner = "募集主：";
    public const string LabelRank = "ランク：";
    public const string LabelParticipants = "参加者：";

    public const string VcToggleOn = "このチャンネルでVCログを出力します。";
    public const string VcToggleOff = "このチャンネルでのVCログを停止しました。";
    public const string VcChannelSet = "このチャンネルをVCログの送信先に設定しました。";
    // 例外発生時に送信するメッセージの候補（ランダムに1つ選んで送信します）
    public static readonly string[] ErrorMessages = new[]
    {
        "エラー発生！たこクオリティだから仕方ないね！",
        "エラー発生！気が向いたら修正します！",
        "エラー発生！壊さないで！優しく使ってね！",
        "エラー発生！直せる人募集中！",
        "エラー発生！このbotは壊れてます！叩けば直るかも！"
    };
}
