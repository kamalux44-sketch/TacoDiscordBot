using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;

namespace TacoDiscordBot.Commands;

public class VcLogCommands : ApplicationCommandModule
{
    [SlashCommand("vclog", "このテキストチャンネルへのVC参加・退出・移動のログ表示を切り替える")]
    public async Task VcLog(InteractionContext ctx)
    {
        var guildId = ctx.Guild?.Id ?? 0UL;

        if (guildId == 0)
        {
            await ctx.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(Strings.CommandGuildOnly)
                    .AsEphemeral(true)
            );

            return;
        }

        if (BotHost.VcLogger.IsConfiguredForGuild(guildId))
        {
            await BotHost.VcLogger.RemoveChannelAsync(guildId);

            await ctx.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(Strings.VcToggleOff)
                    .AsEphemeral(true)
            );

            return;
        }

        await BotHost.VcLogger.SetChannelAsync(guildId, ctx.Channel.Id);

        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent(Strings.VcToggleOn)
                .AsEphemeral(true)
        );
    }
}
