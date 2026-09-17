namespace TacoDiscordBot.Models;

public sealed class BlackjackStatistics
{
    public long Wins { get; init; }

    public long LossHalfUnits { get; init; }

    public long Draws { get; init; }

    public double WinRate => Wins + LossHalfUnits / 2d + Draws == 0
        ? 0
        : Wins / (Wins + LossHalfUnits / 2d + Draws) * 100;
}