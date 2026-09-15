namespace TacoDiscordBot.Models;

public sealed record SlotSymbolConfiguration(
    string Symbol,
    decimal Probability,
    decimal ThreeMatchMultiplier,
    decimal ReachMultiplier
);
