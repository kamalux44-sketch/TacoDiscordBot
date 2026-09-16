using TacoDiscordBot.Models;
using TacoDiscordBot.Repository;

namespace TacoDiscordBot.Services;

public sealed class VcExchangeService
{
    private readonly VcExchangeRepository _repository;
    private readonly RoleService _roleService;

    public VcExchangeService(VcExchangeRepository repository, RoleService? roleService = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _roleService = roleService;
    }

    public Task<VcExchangeSummary> GetSummaryAsync(ulong guildId, ulong userId)
        => _repository.GetSummaryAsync(guildId, userId);

    public async Task<VcExchangeResult> ExchangeAsync(ulong guildId, ulong userId)
    {
        var result = await _repository.ExchangeAsync(guildId, userId);
        if (_roleService != null)
            await _roleService.RefreshUserRolesAsync(guildId, userId);
        return result;
    }

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
