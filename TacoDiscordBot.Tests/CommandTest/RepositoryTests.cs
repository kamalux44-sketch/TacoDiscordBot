using System;
using System.Threading.Tasks;
using Moq;
using TacoDiscordBot.Models;
using TacoDiscordBot.Repository;
using Xunit;

namespace TacoDiscordBot.Tests.CommandTest;

public class RepositoryTests
{
    // VC ログ関連 SQL の構成を検証します。
    // VC ランキング関連 SQL の構成を検証します。
    // 募集関連 SQL の構成を検証します。
    // 接続文字列未設定時の扱いを検証します。

    [Fact]
    public async Task VCログテーブル作成SQLを実行する()
    {
        var baseRepository = CreateBaseRepository();
        var repository = new VcLogRepository(baseRepository.Object);

        await repository.EnsureTableExistsAsync();

        baseRepository.Verify(x => x.ExecuteNonQueryAsync(It.Is<string>(sql =>
            sql.Contains("CREATE TABLE IF NOT EXISTS vc_log_targets"))), Times.Once);
    }

    [Fact]
    public async Task VCログ設定の登録更新SQLを実行する()
    {
        var baseRepository = CreateBaseRepository();
        var repository = new VcLogRepository(baseRepository.Object);

        await repository.SetTargetAsync(10, 20);

        baseRepository.Verify(x => x.ExecuteNonQueryAsync(It.Is<string>(sql =>
            sql.Contains("INSERT INTO vc_log_targets")
            && sql.Contains("VALUES(10, 20)"))), Times.Once);
    }

    [Fact]
    public async Task VCログ設定の削除SQLを実行する()
    {
        var baseRepository = CreateBaseRepository();
        var repository = new VcLogRepository(baseRepository.Object);

        await repository.RemoveTargetAsync(10);

        baseRepository.Verify(x => x.ExecuteNonQueryAsync(It.Is<string>(sql =>
            sql.Contains("DELETE FROM vc_log_targets")
            && sql.Contains("WHERE guild_id = 10"))), Times.Once);
    }

    [Fact]
    public async Task VCランキングテーブル作成SQLを実行する()
    {
        var baseRepository = CreateBaseRepository();
        var repository = new VcRankingRepository(baseRepository.Object);

        await repository.EnsureTableExistsAsync();

        baseRepository.Verify(x => x.ExecuteNonQueryAsync(It.Is<string>(sql =>
            sql.Contains("CREATE TABLE IF NOT EXISTS vc_sessions"))), Times.Once);
    }

    [Fact]
    public async Task 募集を終了状態へ更新するSQLを実行する()
    {
        var baseRepository = CreateBaseRepository();
        var repository = new BoRepository(baseRepository.Object);

        await repository.CloseSessionAsync("session-1");

        baseRepository.Verify(x => x.UseConnectionAsync(It.IsAny<Func<dynamic, Task>>()), Times.Once);
    }

    [Fact]
    public void BaseRepositoryのnull接続文字列を拒否する()
    {
        Assert.Throws<ArgumentNullException>(() => new BaseRepository(null));
    }

    private static Mock<BaseRepository> CreateBaseRepository()
    {
        var repository = new Mock<BaseRepository>("Host=mock") { CallBase = false };
        repository.Setup(x => x.ExecuteNonQueryAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        repository.Setup(x => x.UseConnectionAsync(It.IsAny<Func<dynamic, Task>>()))
            .Returns(Task.CompletedTask);
        return repository;
    }
}
