using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.SlashCommands;
using DSharpPlus.Entities;

namespace TacoDiscordBot.Commands;

public class VcLogCommands : ApplicationCommandModule
{
    [SlashCommand("vclog", "このテキストチャンネルへのVC参加・退出・移動のログ表示を切り替える")]
    public async Task VcLog(InteractionContext ctx)
    {
        // Per-guild vc log targets supported. Toggle or set target for this guild.
        var guildId = ctx.Guild?.Id ?? 0UL;

        if (guildId == 0)
        {
            await ctx.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("このコマンドはサーバー内で実行してください。").AsEphemeral(true));
            return;
        }

        // If a target exists for this guild, remove it (toggle off). Otherwise set this channel as target.
        if (BotHost.VcLogger.IsConfiguredForGuild(guildId))
        {
            await BotHost.VcLogger.RemoveChannelAsync(guildId);
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent(Strings.VcToggleOff).AsEphemeral(true));
        }
        else
        {
            await BotHost.VcLogger.SetChannelAsync(guildId, ctx.Channel.Id);
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent(Strings.VcToggleOn).AsEphemeral(true));
        }
    }
}

