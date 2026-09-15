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

    public async Task<long> GetBalanceAsync(ulong guildId, ulong userId)
        => (await _repository.GetOrCreateAsync(guildId, userId)).Coins;

    public async Task<long> AddCoinsAsync(ulong guildId, ulong userId, long amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        return await _repository.AddCoinsAsync(guildId, userId, amount);
    }

    public async Task<long> RemoveCoinsAsync(ulong guildId, ulong userId, long amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        var balance = await _repository.TryRemoveCoinsAsync(guildId, userId, amount);
        if (!balance.HasValue)
            throw new InvalidOperationException("コインが不足しています。");
        return balance.Value;
    }

    public async Task<bool> TransferAsync(ulong guildId, ulong senderId, ulong receiverId, long coin)
    {
        if (coin <= 0 || senderId == receiverId)
            return false;

        return await _repository.TransferAsync(guildId, senderId, receiverId, coin);
    }

    public async Task<bool> CanAffordAsync(ulong guildId, ulong userId, long amount)
    {
        if (amount <= 0)
            return false;
        return await GetBalanceAsync(guildId, userId) >= amount;
    }

    public async Task<IReadOnlyList<UserData>> GetRankingAsync(ulong guildId)
        => await _repository.GetAllAsync(guildId);
}
