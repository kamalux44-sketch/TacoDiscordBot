using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Contexts;
using TacoDiscordBot.Services.Interface;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Commands;

public class VcRankingCommands : ApplicationCommandModule
{
    [SlashCommand("vcrank", "VC 滞在時間ランキングを表示します")]
    // Slash Command の入力をランキング取得処理へ渡します。
    public async Task VcRank(
        InteractionContext ctx,
        [Option("period", "集計期間: day, week, month, all")] string period = "all"
    )
    {
        // テスト可能なコンテキストへ変換してランキング処理を実行します。
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
        // 集計処理中に Discord の応答がタイムアウトしないよう遅延応答します。
        await response.DeferResponseAsync();

        // ギルド専用コマンドであることを確認します。
        if (guildId == 0)
        {
            await response.EditResponseAsync(Strings.CommandGuildOnly);

            return;
        }

        // ランキングサービスが利用可能か確認します。
        if (service == null)
        {
            await response.EditResponseAsync(Strings.VcRankingServiceNotSet);
            return;
        }

        // 指定された期間のランキングを Embed として組み立てます。
        var embed = await service.BuildRankingEmbedAsync(guildId, period, guild, user);

        await response.EditResponseAsync(embed.Build());
    }
}
