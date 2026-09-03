using System;
using System.Threading.Tasks;
using Moq;
using TacoDiscordBot.Repository;
using Xunit;

namespace TacoDiscordBot.Tests.CommandTest;

public class AiTalkRepositoryTests
{
    // AI 会話テーブルの作成処理を検証します。
    // 対象チャンネル削除用 SQL の構成を検証します。
    // 対象チャンネル登録・更新用 SQL の構成を検証します。

    [Fact]
    public async Task テーブル作成SQLを実行する()
    {
        var baseRepository = new Mock<BaseRepository>("Host=mock") { CallBase = false };
        var repository = new AiTalkRepository(baseRepository.Object);

        await repository.EnsureTableExistsAsync();

        baseRepository.Verify(
            x => x.ExecuteNonQueryAsync(It.Is<string>(sql =>
                sql.Contains("CREATE TABLE IF NOT EXISTS ai_talk_targets")
                && sql.Contains("guild_id BIGINT PRIMARY KEY"))),
            Times.Once
        );
    }

    [Fact]
    public async Task 対象削除SQLを実行しDB接続を直接開かない()
    {
        var baseRepository = new Mock<BaseRepository>("Host=mock") { CallBase = false };
        var repository = new AiTalkRepository(baseRepository.Object);

        await repository.RemoveTargetAsync(123UL);

        baseRepository.Verify(
            x => x.ExecuteNonQueryAsync(It.Is<string>(sql =>
                sql.Contains("DELETE FROM ai_talk_targets")
                && sql.Contains("WHERE guild_id = 123"))),
            Times.Once
        );
        baseRepository.Verify(
            x => x.UseConnectionAsync(It.IsAny<Func<dynamic, Task>>()),
            Times.Never
        );
    }

    [Fact]
    public async Task 対象設定の登録更新SQLを実行する()
    {
        var baseRepository = new Mock<BaseRepository>("Host=mock") { CallBase = false };
        var repository = new AiTalkRepository(baseRepository.Object);

        await repository.SetTargetAsync(123UL, 456UL);

        baseRepository.Verify(x => x.ExecuteNonQueryAsync(It.Is<string>(sql =>
            sql.Contains("INSERT INTO ai_talk_targets")
            && sql.Contains("VALUES(123, 456)")
            && sql.Contains("ON CONFLICT (guild_id)"))), Times.Once);
    }

    [Fact]
    public void nullのBaseRepositoryを拒否する()
    {
        Assert.Throws<ArgumentNullException>(() => new AiTalkRepository(null));
    }
}