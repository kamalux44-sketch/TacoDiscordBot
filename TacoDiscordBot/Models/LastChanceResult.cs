namespace TacoDiscordBot.Models;

public sealed record LastChanceResult(
    long Reward,
    long Balance,
    bool IsJackpot
);
