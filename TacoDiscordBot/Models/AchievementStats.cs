namespace TacoDiscordBot.Models;

public sealed class AchievementStats
{
    public long BankruptcyCount { get; init; }

    public long RareSlotCount { get; init; }

    public long MaxMinesSafeCount { get; init; }

    public long LastChanceJackpotCount { get; init; }

    public long LastChanceZeroCount { get; init; }

    public long BlackjackWinStreak { get; init; }

    public long BlackjackLossStreak { get; init; }
}
