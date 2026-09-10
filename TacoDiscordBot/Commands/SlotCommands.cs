using System.Threading.Tasks;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Contexts;
using TacoDiscordBot.Models;

namespace TacoDiscordBot.Commands;

public sealed class SlotCommands : ApplicationCommandModule
{
    [SlashCommand("slot", "スロットを回します")]
    public async Task Slot(InteractionContext ctx)
    {
        // 抽選結果をEmbed形式で公開します。
        var service = BotHost.SlotService;
        if (service == null)
        {
            await new InteractionResponseContext(ctx).RespondAsync("スロットサービスは未設定です。", true);
            return;
        }

        var result = await service.SpinAsync();
        await new InteractionResponseContext(ctx).RespondAsync(result.Embed);
    }

    [SlashCommand("slotstatus", "スロットの統計を表示します")]
    public async Task SlotStatus(InteractionContext ctx)
    {
        // bot全体で共有されるスロット統計を表示します。
        var service = BotHost.SlotService;
        if (service == null)
        {
            await new InteractionResponseContext(ctx).RespondAsync("スロットサービスは未設定です。", true);
            return;
        }

        var statistics = await service.GetStatisticsAsync();
        await new InteractionResponseContext(ctx).RespondAsync(CreateStatusMessage(statistics));
    }

    private static string CreateStatusMessage(SlotStatistics statistics)
    {
        // 統計コマンド用の簡潔な表示文を作成します。
        return $"**スロット統計**\n"
            + $"最長ハマり：{statistics.LongestHitInterval}回転\n"
            + $"最短当たり：{statistics.ShortestHitInterval?.ToString() ?? "未記録"}回転\n"
            + $"累計回転数：{statistics.TotalSpins}回";
    }
}
