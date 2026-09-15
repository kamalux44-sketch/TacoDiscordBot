using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TacoDiscordBot.Models;
using TacoDiscordBot.Repository;
using TacoDiscordBot.Services.Interface;

namespace TacoDiscordBot.Services;

public sealed class CoinService : ICoinService
{
    private readonly UserDataRepository _repository;

    public CoinService(UserDataRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<long> GetBalanceAsync(ulong userId)
        => (await _repository.GetOrCreateAsync(userId)).Coins;

    public async Task<long> AddCoinsAsync(ulong userId, long amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        return await _repository.AddCoinsAsync(userId, amount);
    }

    public async Task<long> RemoveCoinsAsync(ulong userId, long amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        var balance = await _repository.TryRemoveCoinsAsync(userId, amount);
        if (!balance.HasValue)
            throw new InvalidOperationException("コインが不足しています。");
        return balance.Value;
    }

    public async Task<bool> CanAffordAsync(ulong userId, long amount)
    {
        if (amount <= 0)
            return false;
        return await GetBalanceAsync(userId) >= amount;
    }

    public async Task<IReadOnlyList<UserData>> GetRankingAsync()
        => await _repository.GetAllAsync();
}
