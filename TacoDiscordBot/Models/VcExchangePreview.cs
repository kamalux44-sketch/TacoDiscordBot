namespace TacoDiscordBot.Models;

public sealed record VcExchangePreview(
    ulong GuildId,
    ulong UserId,
    long TotalSeconds,
    long ExchangedSeconds,
    long AvailableSeconds,
    long ExchangeableSeconds,
    long Coins
)
{
    public long RemainingSeconds => AvailableSeconds - ExchangeableSeconds;
    public bool CanExchange => Coins > 0;
}
