using System.Collections.Generic;
using System;

namespace TacoDiscordBot.Models;

public class BoSession
{
    public string SessionId { get; set; }
    public ulong MessageId { get; set; }
    public ulong ChannelId { get; set; }
    public string Game { get; set; }
    public int At { get; set; }
    public string Rank { get; set; }
    // 締め切り（任意、元の入力文字列）
    public string DeadlineRaw { get; set; }
    // 解析された締め切り時刻（UTC）。解析できなければ null
    public DateTime? DeadlineUtc { get; set; }
    // 募集の説明（任意）
    public string Description { get; set; }
    // 締め切り通知済み / 募集終了フラグ
    public bool IsClosed { get; set; }
    public ulong OwnerId { get; set; }
    public List<ulong> Participants { get; set; }
    // 作成日時（UTC）。募集の有効期限判定に使用します。
    public DateTime CreatedAt { get; set; }
}
