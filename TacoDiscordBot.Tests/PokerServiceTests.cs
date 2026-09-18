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
