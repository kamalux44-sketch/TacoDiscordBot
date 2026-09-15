using TacoDiscordBot.Models;
using TacoDiscordBot.Repository;

namespace TacoDiscordBot.Services;

public sealed class VcExchangeService
{
    private readonly VcExchangeRepository _repository;

    public VcExchangeService(VcExchangeRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public Task<VcExchangeSummary> GetSummaryAsync(ulong guildId, ulong userId)
        => _repository.GetSummaryAsync(guildId, userId);

    public Task<VcExchangeResult> ExchangeAsync(ulong guildId, ulong userId)
        => _repository.ExchangeAsync(guildId, userId);

    public static long GetExchangeableMinutes(VcExchangeSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return VcExchangeCalculator.GetExchangeableMinutes(summary.UnexchangedSeconds / 60);
    }

    public static long GetExchangeCoins(VcExchangeSummary summary)
    {
        var exchangeableMinutes = GetExchangeableMinutes(summary);
        return VcExchangeCalculator.GetCoins(exchangeableMinutes);
    }
}
