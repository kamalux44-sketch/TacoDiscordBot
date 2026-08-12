using System.Collections.Generic;

namespace TacoDiscordBot.Models;

public class BoSession
{
    public string SessionId { get; set; }
    public ulong MessageId { get; set; }
    public ulong ChannelId { get; set; }
    public string Game { get; set; }
    public int At { get; set; }
    public string Rank { get; set; }
    public ulong OwnerId { get; set; }
    public List<ulong> Participants { get; set; }
    // 作成日時（UTC）。募集の有効期限判定に使用します。
    public DateTime CreatedAt { get; set; }
}
