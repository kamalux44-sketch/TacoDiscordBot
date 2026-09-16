namespace TacoDiscordBot.Models;

public sealed class LastChanceGame
{
    public LastChanceGame(ulong guildId, ulong userId)
    {
        GuildId = guildId;
        UserId = userId;
    }

    public ulong GuildId { get; }

    public ulong UserId { get; }
}
