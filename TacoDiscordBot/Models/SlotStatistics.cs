namespace TacoDiscordBot.Models;

public sealed class SlotStatistics
{
    public long TotalSpins { get; init; }

    public long? LastHitSpin { get; init; }

    public long LongestHitInterval { get; init; }

    public long? ShortestHitInterval { get; init; }

    public long? LastHitInterval { get; init; }
}
