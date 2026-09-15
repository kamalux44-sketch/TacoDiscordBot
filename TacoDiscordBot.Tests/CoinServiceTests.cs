using System;
using System.Threading.Tasks;
using Moq;
using TacoDiscordBot.Repository;
using TacoDiscordBot.Services;
using Xunit;

namespace TacoDiscordBot.Tests;

public sealed class CoinServiceTests
{
    [Fact]
    public async Task コイン0枚の送金を拒否する()
    {
        var service = CreateService();

        var result = await service.TransferAsync(1, 10, 20, 0);

        Assert.False(result);
    }

    [Fact]
    public async Task 負数コインの送金を拒否する()
    {
        var service = CreateService();

        var result = await service.TransferAsync(1, 10, 20, -100);

        Assert.False(result);
    }

    [Fact]
    public async Task 自分自身への送金を拒否する()
    {
        var service = CreateService();

        var result = await service.TransferAsync(1, 10, 10, 100);

        Assert.False(result);
    }

    [Fact]
    public async Task 送金処理はリポジトリへ委譲する()
    {
        var baseRepository = new Mock<BaseRepository>("Host=mock") { CallBase = false };
        baseRepository
            .Setup(repository => repository.UseTransactionAsync<bool>(It.IsAny<Func<dynamic, dynamic, Task<bool>>>()))
            .ReturnsAsync(false);
        var repository = new UserDataRepository(baseRepository.Object);

        var result = await repository.TransferAsync(1, 10, 20, 100);

        Assert.False(result);
        baseRepository.Verify(
            repository => repository.UseTransactionAsync<bool>(It.IsAny<Func<dynamic, dynamic, Task<bool>>>()),
            Times.Once);
    }

    private static CoinService CreateService()
        => new(new UserDataRepository(new BaseRepository("Host=mock")));
}
