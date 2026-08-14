using System;
using System.Collections.Generic;

namespace TacoDiscordBot.Models;

public class BoSession
{
    // セッションを一意に識別するID
    public string SessionId { get; set; }

    // 募集メッセージのDiscordメッセージID
    public ulong MessageId { get; set; }

    // 募集が作成されたチャンネルのDiscordチャンネルID
    public ulong ChannelId { get; set; }

    // 募集内容
    public string Body { get; set; }

    // 募集人数
    // 募集主は含めず、「追加で何人募集するか」を表します。
    public int At { get; set; }

    // ランク
    public string Rank { get; set; }

    // 締め切りの元入力値
    // 例: "2026-08-13 01:30"
    //
    // 表示用として保持します。
    public string DeadlineRaw { get; set; }

    // 締め切り時刻
    //
    // UTCで保存します。
    // nullの場合は締め切りなしです。
    public DateTime? Deadline { get; set; }

    // 募集の説明
    public string Description { get; set; }

    // 締め切り通知済み / 募集終了フラグ
    public bool IsClosed { get; set; }

    // 募集主のDiscordユーザーID
    public ulong OwnerId { get; set; }

    // 現在の参加者一覧
    public List<ulong> Participants { get; set; }

    // 募集を作成した日時
    //
    // UTCで保存します。
    // 募集作成から7日経過したかどうかの判定に使用します。
    public DateTime CreatedAt { get; set; }
}
