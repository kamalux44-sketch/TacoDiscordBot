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
        // ギルドごとの VC ログ送信先をサポートしています。このギルド用にオン/オフ切替またはチャンネル設定を行います。
        var guildId = ctx.Guild?.Id ?? 0UL;

        if (guildId == 0)
        {
            await ctx.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent(Strings.CommandGuildOnly).AsEphemeral(true));
            return;
        }

        // このギルドにターゲットが存在する場合は削除（オフ切替）。存在しない場合はこのチャンネルをターゲットとして設定します。
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

