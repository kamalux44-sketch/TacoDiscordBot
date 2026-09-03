using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Contexts;
using TacoDiscordBot.Services.Interface;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Commands;

public class AIChannelCommands : ApplicationCommandModule
{
    // AI 会話チャンネル設定コマンドを受け付けます。
    [SlashCommand("aichannel", "このチャンネルを AI 会話チャンネルとして設定します（管理用）")]
    public async Task AiChannel(InteractionContext ctx)
    {
        await AiChannelAsync(
            new InteractionResponseContext(ctx),
            ctx.Guild?.Id ?? 0UL,
            ctx.Channel.Id,
            ctx.Channel.Name,
            BotHost.AiChannelService
        );
    }

    public async Task AiChannelAsync(
        IInteractionResponseContext response,
        ulong guildId,
        ulong channelId,
        string channelName,
        IAiChannelService service
    )
    {
        // ギルドと AI チャンネルサービスの設定を確認してから、対象チャンネルを登録します。

        if (guildId == 0)
        {
            await response.RespondAsync(Strings.CommandGuildOnly, true);

            return;
        }

        if (service == null)
        {
            await response.RespondAsync(
                Strings.AiChannelServiceNotSet
            );

            return;
        }

        await service.SetChannelAsync(guildId, channelId);

        await response.RespondAsync(
            $"このチャンネルを AI 会話チャンネルとして設定しました。 (#{channelName})",
            true
        );
    }
}
