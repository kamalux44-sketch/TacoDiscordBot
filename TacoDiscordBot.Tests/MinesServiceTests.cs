using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services;
using TacoDiscordBot.Services.Interface;
using Xunit;

namespace TacoDiscordBot.Tests;

public sealed class MinesServiceTests
{
    [Fact]
    public async Task 安全マスを開くと倍率がテーブル通りに更新される()
    {
        var coins = new FakeCoinService(1_000);
        var service = new MinesService(coins, () => new[] { 0, 1, 2, 3 });

        await service.StartAsync(1, 10, 100);
        await service.OpenAsync(1, 10, 4);
        var result = await service.OpenAsync(1, 10, 5);

        Assert.Equal(2, result.Game.SafeOpenedCount);
        Assert.Equal(1.5, result.Game.Multiplier);
        Assert.Equal(150, result.Game.CurrentAmount);
    }

    [Fact]
    public async Task 爆弾を開くと掛け金が没収され全ゲームが終了する()
    {
        var coins = new FakeCoinService(1_000);
        var service = new MinesService(coins, () => new[] { 0, 1, 2, 3 });

        await service.StartAsync(1, 10, 100);
        var result = await service.OpenAsync(1, 10, 0);

        Assert.Equal(MinesGameState.Lost, result.Game.State);
        Assert.Equal(0, result.Game.CurrentAmount);
        Assert.Empty(coins.Added);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenAsync(1, 10, 4));
    }

    [Fact]
    public async Task 回収すると現在倍率の払戻を一度だけ行う()
    {
        var coins = new FakeCoinService(1_000);
        var service = new MinesService(coins, () => new[] { 0, 1, 2, 3 });

        await service.StartAsync(1, 10, 100);
        await service.OpenAsync(1, 10, 4);
        var result = await service.CashOutAsync(1, 10);

        Assert.Equal(MinesGameState.CashedOut, result.Game.State);
        Assert.Equal(new[] { 100L }, coins.Added);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CashOutAsync(1, 10));
    }

    [Fact]
    public async Task 全安全マス開放時は5000倍で自動回収する()
    {
        var coins = new FakeCoinService(1_000);
        var service = new MinesService(coins, () => new[] { 0, 1, 2, 3 });

        await service.StartAsync(1, 10, 1);
        MinesResult result = null;
        foreach (var index in Enumerable.Range(4, 16))
            result = await service.OpenAsync(1, 10, index);

        Assert.Equal(MinesGameState.Won, result.Game.State);
        Assert.Equal(5_000, result.Game.CurrentAmount);
        Assert.Equal(new[] { 5_000L }, coins.Added);
    }

    private sealed class FakeCoinService : ICoinService
    {
        public FakeCoinService(long balance) => Balance = balance;

        public long Balance { get; private set; }
        public List<long> Added { get; } = new();

        public Task<long> GetBalanceAsync(ulong guildId, ulong userId) => Task.FromResult(Balance);

        public Task<long> AddCoinsAsync(ulong guildId, ulong userId, long amount)
        {
            Added.Add(amount);
            Balance += amount;
            return Task.FromResult(Balance);
        }

        public Task<long> RemoveCoinsAsync(ulong guildId, ulong userId, long amount)
        {
            if (Balance < amount)
                throw new InvalidOperationException("コインが不足しています。");
            Balance -= amount;
            return Task.FromResult(Balance);
        }

        public Task<bool> TransferAsync(ulong guildId, ulong senderId, ulong receiverId, long coin)
            => Task.FromResult(false);

        public Task<bool> CanAffordAsync(ulong guildId, ulong userId, long amount)
            => Task.FromResult(Balance >= amount);

        public Task<IReadOnlyList<UserData>> GetTopRankingAsync(ulong guildId, int limit)
            => Task.FromResult<IReadOnlyList<UserData>>(Array.Empty<UserData>());

        public Task<IReadOnlyList<UserData>> GetRankingAsync(ulong guildId)
            => Task.FromResult<IReadOnlyList<UserData>>(Array.Empty<UserData>());
    }
}
