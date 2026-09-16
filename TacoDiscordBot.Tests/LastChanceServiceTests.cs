using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TacoDiscordBot.Services;
using TacoDiscordBot.Services.Interface;
using Xunit;

namespace TacoDiscordBot.Tests;

public sealed class LastChanceServiceTests
{
    [Fact]
    public async Task 所持コイン0のユーザーだけが開始できる()
    {
        var store = new FakeLastChanceStore(canStart: false);
        var service = new LastChanceService(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(1, 10));
        Assert.Equal(0, store.StartCount);
    }

    [Fact]
    public async Task 安定を選ぶと1000コインを一度だけ付与する()
    {
        var store = new FakeLastChanceStore();
        var service = new LastChanceService(store);

        await service.StartAsync(1, 10);
        var result = await service.SelectAsync(1, 10, LastChanceChoice.Stable);

        Assert.Equal(1_000, result.Reward);
        Assert.Equal(1_000, result.Balance);
        Assert.Equal(new[] { 1_000L }, store.Rewards);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SelectAsync(1, 10, LastChanceChoice.Stable)
        );
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    [InlineData(10, 500)]
    [InlineData(34, 500)]
    [InlineData(35, 1_000)]
    [InlineData(64, 1_000)]
    [InlineData(65, 1_500)]
    [InlineData(84, 1_500)]
    [InlineData(85, 2_000)]
    [InlineData(94, 2_000)]
    [InlineData(95, 3_000)]
    public void ギャンブル報酬は指定確率テーブルになる(int randomValue, long expectedReward)
    {
        Assert.Equal(expectedReward, LastChanceService.DetermineGambleReward(randomValue));
    }

    [Fact]
    public async Task 一発逆転は60パーセントで0になり40パーセントでJACKPOTになる()
    {
        var store = new FakeLastChanceStore();
        var randomValues = new Queue<int>(new[] { 59, 60 });
        var service = new LastChanceService(store, () => randomValues.Dequeue());

        await service.StartAsync(1, 10);
        var miss = await service.SelectAsync(1, 10, LastChanceChoice.Jackpot);
        Assert.Equal(0, miss.Reward);
        Assert.False(miss.IsJackpot);

        await service.StartAsync(1, 10);
        var jackpot = await service.SelectAsync(1, 10, LastChanceChoice.Jackpot);
        Assert.Equal(5_000, jackpot.Reward);
        Assert.True(jackpot.IsJackpot);
    }

    private sealed class FakeLastChanceStore : ILastChanceStore
    {
        private readonly bool _canStart;

        public FakeLastChanceStore(bool canStart = true) => _canStart = canStart;

        public int StartCount { get; private set; }
        public List<long> Rewards { get; } = new();

        public Task<bool> TryStartAsync(ulong guildId, ulong userId)
        {
            if (_canStart)
                StartCount++;
            return Task.FromResult(_canStart);
        }

        public Task<long?> CompleteAsync(ulong guildId, ulong userId, long reward)
        {
            Rewards.Add(reward);
            return Task.FromResult<long?>(reward);
        }
    }
}
