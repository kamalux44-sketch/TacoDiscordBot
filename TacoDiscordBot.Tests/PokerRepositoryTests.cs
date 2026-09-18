using System.Threading.Tasks;
using Moq;
using TacoDiscordBot.Repository;
using Xunit;

namespace TacoDiscordBot.Tests;

public sealed class PokerRepositoryTests
{
    [Fact]
    public async Task Poker用テーブル作成SQLを実行する()
    {
        var baseRepository = new Mock<BaseRepository>("Host=mock") { CallBase = false };
        baseRepository
            .Setup(repository => repository.ExecuteNonQueryAsync(It.Is<string>(sql =>
                sql.Contains("poker_games") && sql.Contains("poker_players"))))
            .Returns(Task.CompletedTask);
        var repository = new PokerRepository(baseRepository.Object);

        await repository.EnsureTablesExistAsync();

        baseRepository.Verify(
            repository => repository.ExecuteNonQueryAsync(It.Is<string>(sql =>
                sql.Contains("CREATE TABLE IF NOT EXISTS poker_games")
                && sql.Contains("CREATE TABLE IF NOT EXISTS poker_players"))),
            Times.Once);
    }
}
