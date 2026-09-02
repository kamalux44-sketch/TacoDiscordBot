using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Services;
using TacoDiscordBot.Contexts;

namespace TacoDiscordBot.Commands;

public class VcRankingCommands : ApplicationCommandModule
{
    [SlashCommand("vcrank", "VC 滞在時間ランキングを表示します")]
    public async Task VcRank(
        InteractionContext ctx,
        [Option("period", "集計期間: day, week, month, all")] string period = "all"
    )
    {
        await VcRankAsync(
            new InteractionResponseContext(ctx),
            ctx.Guild?.Id ?? 0UL,
            ctx.Guild,
            ctx.User,
            period,
            BotHost.VcRankingService
        );
    }

    public async Task VcRankAsync(
        IInteractionResponseContext response,
        ulong guildId,
        DiscordGuild guild,
        DiscordUser user,
        string period,
        IVcRankingService service
    )
    {
        await response.DeferResponseAsync();

        if (guildId == 0)
        {
            await response.EditResponseAsync(Strings.CommandGuildOnly);

            return;
        }

        if (service == null)
        {
            await response.EditResponseAsync("VCランキングサービスは未設定です。");
            return;
        }

        var embed = await service.BuildRankingEmbedAsync(guildId, period, guild, user);

        await response.EditResponseAsync(embed.Build());
    }
}
