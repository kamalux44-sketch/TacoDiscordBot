using System.Threading.Tasks;
using DSharpPlus.SlashCommands;
using DSharpPlus.Entities;

namespace TacoDiscordBot.Commands;

public class VcCommands : ApplicationCommandModule
{
    [SlashCommand("vclog", "Toggle VC join/leave/move logging to this text channel")]
    public async Task VcLog(InteractionContext ctx)
    {
        // 単一サーバー運用を想定しています。
        // まず VC ログ先チャンネルが設定されているかを確認します。
        if (!BotHost.VcLogger.IsConfigured)
        {
            // 環境変数が未設定の場合は、このチャンネルを送信先として設定します（メモリのみ）。
            BotHost.VcLogger.SetChannel(ctx.Channel.Id);
            await ctx.Channel.SendMessageAsync(Strings.VcChannelSet);
            return;
        }

        // 既にチャンネルが設定されている場合はオン/オフをトグルします。
        var enabled = BotHost.VcLogger.ToggleChannel();
        await ctx.Channel.SendMessageAsync(enabled ? Strings.VcToggleOn : Strings.VcToggleOff);
    }
}
