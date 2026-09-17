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
    [SlashCommand("vcchannel", "このチャンネルへのVCログ表示を切り替えます（管理用）")]
    public async Task VcChannel(InteractionContext ctx)
    {
        await VcChannelAsync(
            new InteractionResponseContext(ctx),
            ctx.Guild?.Id ?? 0UL,
            ctx.Channel.Id,
            BotHost.VcLogger,
            ctx.Member?.Permissions.HasPermission(Permissions.Administrator) == true
        );
    }

    [SlashCommand("vclog", "このチャンネルへのVCログ表示を切り替えます（管理用）")]
    public Task VcLog(InteractionContext ctx) => VcChannel(ctx);

    public async Task VcLogAsync(
        IInteractionResponseContext response,
        ulong guildId,
        ulong channelId,
        IVcLogService logger,
        bool isAdministrator = true
    )
        => await VcChannelAsync(response, guildId, channelId, logger, isAdministrator);

    public async Task VcChannelAsync(
        IInteractionResponseContext response,
        ulong guildId,
        ulong channelId,
        IVcLogService logger,
        bool isAdministrator = true
    )
    {
        // ギルド専用コマンドであることを確認します。
        if (guildId == 0)
        {
            await response.RespondAsync(Strings.CommandGuildOnly, true);

            return;
        }

        if (!isAdministrator)
        {
            await response.RespondAsync("このコマンドは管理者のみ実行できます。", true);
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
