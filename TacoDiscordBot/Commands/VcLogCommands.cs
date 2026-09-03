using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Contexts;
using TacoDiscordBot.Services.Interface;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Commands;

public class VcLogCommands : ApplicationCommandModule
{
    [SlashCommand("vclog", "このテキストチャンネルへのVC参加・退出・移動のログ表示を切り替える")]
    // Slash Command の入力を VC ログ設定処理へ渡します。
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
        // ギルド専用コマンドであることを確認します。
        if (guildId == 0)
        {
            await response.RespondAsync(Strings.CommandGuildOnly, true);

            return;
        }

        // VC ログサービスが利用可能か確認します。
        if (logger == null)
        {
            await response.RespondAsync(Strings.VcLogServiceNotSet, true);
            return;
        }

        // 設定済みなら解除し、未設定なら現在のチャンネルを登録します。
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
