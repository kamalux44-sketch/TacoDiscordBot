namespace TacoDiscordBot.Models;

public sealed class UserData
{
    public ulong GuildId { get; init; }

    public ulong UserId { get; init; }

    public long Coins { get; init; }

    public long LastChanceCount { get; init; }
}
