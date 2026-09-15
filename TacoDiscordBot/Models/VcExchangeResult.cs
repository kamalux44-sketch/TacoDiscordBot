namespace TacoDiscordBot.Models;

public sealed record VcExchangeResult(
    bool Success,
    long ExchangeMinutes,
    long Coins,
    long RemainingMinutes
);
