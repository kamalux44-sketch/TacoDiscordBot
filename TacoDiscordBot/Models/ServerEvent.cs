using System;

namespace TacoDiscordBot.Models;

public enum EventType
{
    DoublePayout,
    HotSlot,
    LossBack,
    GoodLuck,
    BlackjackInsurance,
    DoubleUpBoost,
    MinesSafetyOne,
    MinesSafetyTwo,
    MinesSafetyThree,
    LiveOrDie
}

public sealed record ServerEvent(
    ulong GuildId,
    EventType Type,
    ulong ActivatorId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndsAt,
    bool IsPersonal = false
)
{
    public bool IsActive(DateTimeOffset now) => EndsAt > now;
}

public sealed record EventDefinition(
    EventType Type,
    string Name,
    string Description,
    TimeSpan Duration,
    decimal TopRankingRate,
    long FixedCost,
    bool IsPersonal = false
);

public sealed record EventEffects(
    decimal PayoutMultiplier = 1m,
    decimal DoubleUpMultiplier = 2m,
    decimal SurrenderRefundRate = 0m,
    decimal LoseRefundRate = 0m,
    int MinesBombReduction = 0,
    bool HasPersonalRisk = false
);

public sealed record EventCost(long Amount, string Description);
