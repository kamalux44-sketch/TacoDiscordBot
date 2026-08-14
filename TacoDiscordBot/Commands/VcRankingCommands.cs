using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands.Attributes;
using TacoDiscordBot.Services;

namespace TacoDiscordBot.Commands;

public class VcRankingCommands : ApplicationCommandModule
{
    [SlashCommand("vcrank", "VC 滞在時間ランキングを表示します")]
    public async Task VcRank(
        InteractionContext ctx,
        [Option("period", "集計期間: day, week, month, all")] string period = "day")
    {
        // ACK the interaction to avoid "application did not respond"
        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);

        var guildId = ctx.Guild?.Id ?? 0UL;
        if (guildId == 0)
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(Strings.CommandGuildOnly));
            return;
        }

        var svc = new VcRankingService();
        var embed = await svc.BuildRankingEmbedAsync(guildId, period, ctx.Guild, ctx.User);
        await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed.Build()));
    }
}

