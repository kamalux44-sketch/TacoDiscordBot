using System;
using System.Threading.Tasks;
using TacoDiscordBot.Models;
using TacoDiscordBot.Repository;
using TacoDiscordBot.Services;
using Xunit;

namespace TacoDiscordBot.Tests;

public sealed class PokerServiceTests
{
    [Fact]
    public async Task 最初のプレイヤーだけのCheckではカード交換へ進まない()
    {
        var service = CreateService();
        var game = await CreateStartedGameAsync(service);

        await service.ActAsync(game.TableId, 10, PokerAction.Check);

        Assert.Equal(PokerPhase.BetRound1, game.Phase);
        Assert.Equal((ulong)20, game.Players[game.CurrentPlayerIndex].UserId);
    }

    [Fact]
    public async Task 全員のCheck後にカード交換へ進む()
    {
        var service = CreateService();
        var game = await CreateStartedGameAsync(service);

        await service.ActAsync(game.TableId, 10, PokerAction.Check);
        await service.ActAsync(game.TableId, 20, PokerAction.Check);

        Assert.Equal(PokerPhase.Exchange, game.Phase);
        Assert.Equal(-1, game.CurrentPlayerIndex);
    }

    [Fact]
    public async Task ゲーム終了時のスナップショットに全員の手札と役が含まれる()
    {
        var service = CreateService();
        var game = await CreateStartedGameAsync(service);

        await service.ActAsync(game.TableId, 10, PokerAction.Check);
        await service.ActAsync(game.TableId, 20, PokerAction.Check);
        await service.ExchangeAsync(game.TableId, 10, []);
        await service.ExchangeAsync(game.TableId, 20, []);
        await service.ActAsync(game.TableId, 10, PokerAction.Check);
        await service.ActAsync(game.TableId, 20, PokerAction.Check);

        var snapshot = service.GetSnapshot(game.TableId);

        Assert.Equal(PokerPhase.Finished, snapshot.Phase);
        Assert.All(snapshot.Players, player =>
        {
            Assert.Equal(5, player.Hand.Count);
            Assert.False(string.IsNullOrWhiteSpace(player.HandCategory));
        });
    }

    [Fact]
    public async Task 開始前は卓作成者が卓を終了できる()
    {
        var service = CreateService();
        var game = await service.CreateGameAsync(1, 10, 5, 1);
        game.Players.Add(new PokerPlayer(10, "Player 1", PokerService.InitialChips));

        await service.EndAsync(game.TableId, 10);

        Assert.Equal(PokerPhase.Finished, game.Phase);
        Assert.Equal("開始前に卓が終了しました", game.WinnerText);
        Assert.Equal(-1, game.CurrentPlayerIndex);
    }

    [Fact]
    public async Task ゲーム中の強制終了ではPotを参加者へ返却する()
    {
        var service = CreateService();
        var game = await CreateStartedGameAsync(service);
        game.Players[0].Chips -= 150;
        game.Players[1].Chips -= 150;
        game.Pot = 300;

        await service.EndAsync(game.TableId, 10);

        Assert.Equal(PokerPhase.Finished, game.Phase);
        Assert.Equal("ゲームが強制終了されました", game.WinnerText);
        Assert.Equal(0, game.Pot);
        Assert.All(game.Players, player => Assert.Equal(PokerService.InitialChips, player.Chips));
    }

    [Fact]
    public async Task 卓作成者以外は卓を終了できない()
    {
        var service = CreateService();
        var game = await service.CreateGameAsync(1, 10, 5, 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EndAsync(game.TableId, 20));
    }

    private static PokerService CreateService()
        => new(new CoinService(new UserDataRepository(new BaseRepository("Host=mock"))));

    private static async Task<PokerGame> CreateStartedGameAsync(PokerService service)
    {
        var game = await service.CreateGameAsync(1, 10, 1, 1);
        game.Players.Add(new PokerPlayer(10, "Player 1", PokerService.InitialChips));
        game.Players.Add(new PokerPlayer(20, "Player 2", PokerService.InitialChips));
        return await service.StartAsync(game.TableId, 10);
    }
}
