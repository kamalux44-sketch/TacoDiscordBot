using System.Threading.Tasks;
using DSharpPlus.SlashCommands;
using DSharpPlus.Entities;
using TacoDiscordBot.Services;

namespace TacoDiscordBot.Commands;

public class VcRankingCommands : ApplicationCommandModule
{
    [SlashCommand("vcrank", "VC 滞在時間ランキングを表示します")]
    public async Task VcRank(
        InteractionContext ctx,
        [Option("period", "集計期間: day, week, month, all")] string period = "day")
    {
        var guildId = ctx.Guild?.Id ?? 0UL;
        if (guildId == 0)
        {
            await ctx.Channel.SendMessageAsync("サーバー内で実行してください。");
            return;
        }

        var svc = new VcRankingService();
        var embed = await svc.BuildRankingEmbedAsync(guildId, period, ctx.Guild, ctx.User);
        await ctx.Channel.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(embed));
    }
}

