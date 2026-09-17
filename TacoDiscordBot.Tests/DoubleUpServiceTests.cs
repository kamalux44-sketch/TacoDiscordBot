using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services;
using TacoDiscordBot.Services.Interface;
using Xunit;

namespace TacoDiscordBot.Tests;

public sealed class DoubleUpServiceTests
{
    [Fact]
    public async Task HIGH選択でHIGHが出ると賞金が2倍になる()
    {
        var coins = new FakeCoinService(1_000);
        var service = CreateService(coins, 10);

        await service.StartAsync(1, 10, 100);
        var result = await service.SelectAsync(1, 10, DoubleUpChoice.High);

        Assert.Equal(200, result.CurrentAmount);
        Assert.Equal(DoubleUpState.Won, result.State);
        Assert.Equal(1, coins.Removed.Count);
        Assert.Empty(coins.Added);
    }

    [Fact]
    public async Task LOW選択でLOWが出ると賞金が2倍になる()
    {
        var service = CreateService(new FakeCoinService(1_000), 4);

        await service.StartAsync(1, 10, 100);
        var result = await service.SelectAsync(1, 10, DoubleUpChoice.Low);

        Assert.Equal(200, result.CurrentAmount);
        Assert.Equal(DoubleUpState.Won, result.State);
    }

    [Fact]
    public async Task 選択と結果が不一致ならゲーム終了して賞金が0になる()
    {
        var coins = new FakeCoinService(1_000);
        var service = CreateService(coins, 4);

        await service.StartAsync(1, 10, 100);
        var result = await service.SelectAsync(1, 10, DoubleUpChoice.High);

        Assert.Equal(0, result.CurrentAmount);
        Assert.Equal(DoubleUpState.Lost, result.State);
        await service.StartAsync(1, 10, 100);
        Assert.Equal(2, coins.Removed.Count);
    }

    [Fact]
    public async Task セブンが出ると賞金を維持して再選択できる()
    {
        var service = CreateService(new FakeCoinService(1_000), 7, 10);

        await service.StartAsync(1, 10, 100);
        var seven = await service.SelectAsync(1, 10, DoubleUpChoice.High);
        var win = await service.SelectAsync(1, 10, DoubleUpChoice.High);

        Assert.Equal(100, seven.CurrentAmount);
        Assert.Equal(7, seven.Number);
        Assert.Equal(DoubleUpState.Selecting, seven.State);
        Assert.Equal(200, win.CurrentAmount);
    }

    [Fact]
    public async Task 連続7の回数を管理する()
    {
        var service = CreateService(new FakeCoinService(1_000), 7, 7, 7);

        await service.StartAsync(1, 10, 100);
        var first = await service.SelectAsync(1, 10, DoubleUpChoice.Low);
        var second = await service.SelectAsync(1, 10, DoubleUpChoice.Low);
        var third = await service.SelectAsync(1, 10, DoubleUpChoice.Low);

        Assert.Contains("SEVEN × 2", second.Embed.Description);
        Assert.Contains("SEVEN × 3", third.Embed.Description);
        Assert.Contains("LUCKY GOD MODE", third.Embed.Description);
        Assert.Equal(100, first.CurrentAmount);
    }

    [Fact]
    public async Task CASHOUTは賞金を一度だけ加算してゲーム終了する()
    {
        var coins = new FakeCoinService(1_000);
        var service = CreateService(coins, 10);

        await service.StartAsync(1, 10, 100);
        await service.SelectAsync(1, 10, DoubleUpChoice.High);
        var result = await service.CashOutAsync(1, 10);

        Assert.Equal(DoubleUpState.CashedOut, result.State);
        Assert.Equal(new[] { 200L }, coins.Added);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CashOutAsync(1, 10));
    }

    [Fact]
    public async Task CASHOUTでは初期ベットにイベント倍率を適用しない()
    {
        var coins = new FakeCoinService(1_000_000);
        var eventManager = new EventManager(coins);
        await eventManager.StartEventAsync(1, 10, EventType.DoublePayout);
        var service = CreateService(coins, eventManager, 10);

        await service.StartAsync(1, 10, 100);
        await service.SelectAsync(1, 10, DoubleUpChoice.High);
        await service.CashOutAsync(1, 10);

        Assert.Equal(new[] { 250L }, coins.Added);
    }

    [Fact]
    public async Task 最大倍率は128倍でそれ以上抽選できない()
    {
        var coins = new FakeCoinService(10_000);
        var service = CreateService(coins, Enumerable.Repeat(10, 7).ToArray());

        await service.StartAsync(1, 10, 1);
        DoubleUpResult result = null;
        for (var index = 0; index < 7; index++)
            result = await service.SelectAsync(1, 10, DoubleUpChoice.High);

        Assert.True(result.IsMaxMultiplier);
        Assert.Equal(128, result.CurrentAmount);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SelectAsync(1, 10, DoubleUpChoice.High));
    }

    [Fact]
    public async Task ゲーム中の二重起動を拒否して残高を二重減算しない()
    {
        var coins = new FakeCoinService(1_000);
        var service = CreateService(coins, 10);

        await service.StartAsync(1, 10, 100);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(1, 10, 100));

        Assert.Single(coins.Removed);
    }

    private static DoubleUpService CreateService(
        FakeCoinService coins,
        params int[] numbers)
        => CreateService(coins, null, numbers);

    private static DoubleUpService CreateService(
        FakeCoinService coins,
        EventManager? eventManager,
        params int[] numbers)
    {
        var queue = new Queue<int>(numbers);
        return new DoubleUpService(
            coins,
            () => queue.Dequeue(),
            _ => Task.CompletedTask,
            eventManager: eventManager
        );
    }

    private sealed class FakeCoinService : ICoinService
    {
        public FakeCoinService(long balance)
        {
            Balance = balance;
        }

        public long Balance { get; private set; }
        public List<long> Removed { get; } = new();
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
            Removed.Add(amount);
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
