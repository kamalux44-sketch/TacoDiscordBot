namespace TacoDiscordBot.Models;

public sealed record VcExchangeSummary(
    long TotalSeconds,
    long ExchangedSeconds
)
{
    public long UnexchangedSeconds => Math.Max(0, TotalSeconds - ExchangedSeconds);
}
