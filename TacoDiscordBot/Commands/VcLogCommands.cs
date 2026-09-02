using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Contexts;
using TacoDiscordBot.Services;

namespace TacoDiscordBot.Commands;

public class VcLogCommands : ApplicationCommandModule
{
    [SlashCommand("vclog", "このテキストチャンネルへのVC参加・退出・移動のログ表示を切り替える")]
    public async Task VcLog(InteractionContext ctx)
    {
        await VcLogAsync(
            new InteractionResponseContext(ctx),
            ctx.Guild?.Id ?? 0UL,
            ctx.Channel.Id,
            BotHost.VcLogger
        );
    }

    public async Task VcLogAsync(
        IInteractionResponseContext response,
        ulong guildId,
        ulong channelId,
        IVcLogger logger
    )
    {

        if (guildId == 0)
        {
            await response.RespondAsync(Strings.CommandGuildOnly, true);

            return;
        }

        if (logger == null)
        {
            await response.RespondAsync("VCログサービスは未設定です。", true);
            return;
        }

        if (logger.IsConfiguredForGuild(guildId))
        {
            await logger.RemoveChannelAsync(guildId);

            await response.RespondAsync(Strings.VcToggleOff, true);

            return;
        }

        await logger.SetChannelAsync(guildId, channelId);

        await response.RespondAsync(Strings.VcToggleOn, true);
    }
}
