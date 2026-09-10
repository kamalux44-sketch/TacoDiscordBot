namespace TacoDiscordBot.Models;

public sealed class BirthdayRecord
{
    public ulong UserId { get; init; }

    public int? Year { get; init; }

    public int Month { get; init; }

    public int Day { get; init; }
}
