using System.Threading.Tasks;
using DSharpPlus.SlashCommands;
using DSharpPlus.Entities;

namespace TacoDiscordBot.Commands;

public class VcLogCommands : ApplicationCommandModule
{
    [SlashCommand("vclog", "Toggle VC join/leave/move logging to this text channel")]
    public async Task VcLog(InteractionContext ctx)
    {
        // Per-guild vc log targets supported. Toggle or set target for this guild.
        var guildId = ctx.Guild?.Id ?? 0UL;

        if (guildId == 0)
        {
            await ctx.Channel.SendMessageAsync("このコマンドはサーバー内で実行してください。");
            return;
        }

        // If a target exists for this guild, remove it (toggle off). Otherwise set this channel as target.
        if (BotHost.VcLogger.IsConfiguredForGuild(guildId))
        {
            await BotHost.VcLogger.RemoveChannelAsync(guildId);
            await ctx.Channel.SendMessageAsync(Strings.VcToggleOff);
        }
        else
        {
            await BotHost.VcLogger.SetChannelAsync(guildId, ctx.Channel.Id);
            await ctx.Channel.SendMessageAsync(Strings.VcToggleOn);
        }
    }
}
