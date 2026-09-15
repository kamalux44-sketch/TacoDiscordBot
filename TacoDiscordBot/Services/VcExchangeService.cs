using System;
using System.Threading.Tasks;
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

    public Task<VcExchangePreview> GetPreviewAsync(ulong guildId, ulong userId)
        => _repository.GetPreviewAsync(guildId, userId);

    public Task<VcExchangeResult?> ExchangeAsync(ulong guildId, ulong userId)
        => _repository.ExchangeAsync(guildId, userId);
}
