using System.Threading.Tasks;
using TacoDiscordBot.Services;
using Xunit;

namespace TacoDiscordBot.Tests.CommandTest;

public class VcRankingServiceTests
{
    [Fact]
    public async Task DB未設定の場合はエラー内容を含むランキングEmbedを返す()
    {
        var previousHost = System.Environment.GetEnvironmentVariable("PGHOST");
        System.Environment.SetEnvironmentVariable("PGHOST", null);

        try
        {
            var service = new VcRankingService();
            var embed = await service.BuildRankingEmbedAsync(10, "all", null, null);

            Assert.Equal("VCランキング", embed.Title);
            Assert.Contains("Postgres 未接続", embed.Description);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("PGHOST", previousHost);
        }
    }

    [Fact]
    public async Task DB未設定の場合は音声状態更新を安全に終了する()
    {
        var previousHost = System.Environment.GetEnvironmentVariable("PGHOST");
        System.Environment.SetEnvironmentVariable("PGHOST", null);

        try
        {
            var service = new VcRankingService();

            await service.HandleVoiceStateUpdated(null, null);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("PGHOST", previousHost);
        }
    }
}
