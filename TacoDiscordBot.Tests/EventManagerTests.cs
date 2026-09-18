using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services;
using TacoDiscordBot.Services.Interface;
using Xunit;

namespace TacoDiscordBot.Tests;

public sealed class EventManagerTests
{
    [Fact]
    public async Task TOP10総資産の割合から発動コストを計算する()
    {
        var coinService = new FakeCoinService(100_000)
        {
            Ranking =
            [
                new UserData { Coins = 100_000 },
                new UserData { Coins = 50_000 }
            ]
        };
        var manager = new EventManager(coinService);

        var cost = await manager.CalculateCostAsync(1, 10, EventType.DoublePayout);

        Assert.Equal(75_000, cost.Amount);
    }

    [Fact]
    public async Task サーバーイベントは同一ギルドで同時発動できない()
    {
        var manager = new EventManager(new FakeCoinService(1_000_000)
        {
            Ranking = [new UserData { Coins = 100_000 }]
        });

        await manager.StartEventAsync(1, 10, EventType.DoublePayout);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.StartEventAsync(1, 20, EventType.GoodLuck));

        Assert.Equal("現在「💰 倍返しキャンペーン！」が発動中のため実行できません。", exception.Message);
    }

    [Fact]
    public async Task 個人イベントはサーバーイベントと重複できる()
    {
        var manager = new EventManager(new FakeCoinService(1_000_000)
        {
            Ranking = [new UserData { Coins = 100_000 }]
        });

        await manager.StartEventAsync(1, 10, EventType.DoublePayout);
        await manager.StartEventAsync(1, 20, EventType.LiveOrDie);

        Assert.True(manager.HasPersonalEvent(1, 20));
        Assert.Equal(1.5m, manager.CalculatePayout(1, 10, 100, "blackjack") / 100m);
        Assert.Equal(5m, manager.CalculatePayout(1, 20, 100, "blackjack") / 100m);
        Assert.Equal(1.5m, manager.CalculatePayout(1, 20, 100, "slot") / 100m);
    }

    [Theory]
    [InlineData(EventType.MinesSafetyOne, 15, 1)]
    [InlineData(EventType.MinesSafetyTwo, 10, 2)]
    [InlineData(EventType.MinesSafetyThree, 5, 3)]
    public async Task Mines安全週間はユーザーごとの上限回数まで爆弾を減らす(
        EventType eventType,
        int limit,
        int expectedReduction)
    {
        var manager = new EventManager(new FakeCoinService(1_000_000));

        await manager.StartEventAsync(1, 10, eventType);

        for (var count = 0; count < limit; count++)
            Assert.Equal(expectedReduction, manager.GetMinesBombReduction(1, 10));

        Assert.Equal(0, manager.GetMinesBombReduction(1, 10));
        Assert.Equal(expectedReduction, manager.GetMinesBombReduction(1, 20));
    }

    [Fact]
    public async Task 倍率対象外の返金額にはイベント倍率を適用しない()
    {
        var manager = new EventManager(new FakeCoinService(1_000_000)
        {
            Ranking = [new UserData { Coins = 100_000 }]
        });

        await manager.StartEventAsync(1, 10, EventType.DoublePayout);

        Assert.Equal(100, manager.CalculatePayout(1, 10, 100, "doubleup", 100));
        Assert.Equal(250, manager.CalculatePayout(1, 10, 200, "doubleup", 100));
    }

    [Fact]
    public async Task ブラックジャック保険は所持金の50パーセントを発動コストとする()
    {
        var coins = new FakeCoinService(100_000);
        var manager = new EventManager(coins);

        var cost = await manager.CalculateCostAsync(1, 10, EventType.BlackjackInsurance);

        Assert.Equal(50_000, cost.Amount);
        Assert.Equal("発動者の所持金の50%", cost.Description);
    }

    [Fact]
    public async Task ブラックジャック保険はサレンダー時にベットの80パーセントを返還する()
    {
        var coins = new FakeCoinService(100_000);
        var manager = new EventManager(coins);
        await manager.StartEventAsync(1, 10, EventType.BlackjackInsurance);

        var effects = manager.GetEffects(1, 10);

        Assert.Equal(0.8m, effects.SurrenderRefundRate);
    }

    [Fact]
    public async Task 個人イベントの敗北処理は敗北後残高の50パーセントを徴収して消費する()
    {
        var coins = new FakeCoinService(1_000);
        var manager = new EventManager(coins);
        await manager.StartEventAsync(1, 10, EventType.LiveOrDie);

        var deducted = await manager.ResolvePersonalLossAsync(1, 10);

        Assert.Equal(250, deducted);
        Assert.Equal(250, coins.Balance);
        Assert.False(manager.HasPersonalEvent(1, 10));
    }

    private sealed class FakeCoinService : ICoinService
    {
        public FakeCoinService(long balance) => Balance = balance;
        public long Balance { get; private set; }
        public IReadOnlyList<UserData> Ranking { get; init; } = Array.Empty<UserData>();

        public Task<long> GetBalanceAsync(ulong guildId, ulong userId) => Task.FromResult(Balance);
        public Task<UserData> GetUserDataAsync(ulong guildId, ulong userId)
            => Task.FromResult(new UserData { GuildId = guildId, UserId = userId, Coins = Balance });
        public Task<long> AddCoinsAsync(ulong guildId, ulong userId, long amount)
        {
            Balance += amount;
            return Task.FromResult(Balance);
        }
        public Task<long> RemoveCoinsAsync(ulong guildId, ulong userId, long amount)
        {
            if (amount <= 0 || Balance < amount)
                throw new InvalidOperationException("コインが不足しています。");
            Balance -= amount;
            return Task.FromResult(Balance);
        }
        public Task<bool> TransferAsync(ulong guildId, ulong senderId, ulong receiverId, long coin)
            => Task.FromResult(false);
        public Task<bool> CanAffordAsync(ulong guildId, ulong userId, long amount)
            => Task.FromResult(Balance >= amount);
        public Task<IReadOnlyList<UserData>> GetRankingAsync(ulong guildId)
            => Task.FromResult(Ranking);
        public Task<IReadOnlyList<UserData>> GetTopRankingAsync(ulong guildId, int limit)
            => Task.FromResult(Ranking);
    }
}
