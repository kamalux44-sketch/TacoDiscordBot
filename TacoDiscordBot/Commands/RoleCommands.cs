using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Contexts;

namespace TacoDiscordBot.Commands;

public sealed class RoleCommands : ApplicationCommandModule
{
    [SlashCommand("rolechannel", "ロール解除通知の投稿先をこのチャンネルに設定します")]
    public async Task RoleChannel(InteractionContext ctx)
    {
        if (ctx.Guild == null)
        {
            await new InteractionResponseContext(ctx).RespondAsync("このコマンドはサーバー内で実行してください。", true);
            return;
        }

        if (ctx.Member == null || !ctx.Member.Permissions.HasPermission(Permissions.Administrator))
        {
            await new InteractionResponseContext(ctx).RespondAsync("このコマンドは管理者のみ実行できます。", true);
            return;
        }

        var service = BotHost.RoleService;
        if (service == null)
        {
            await new InteractionResponseContext(ctx).RespondAsync("ロールサービスは未設定です。", true);
            return;
        }
        var enabled = await service.ToggleNotificationChannelAsync(ctx.Guild.Id, ctx.Channel.Id);
        await new InteractionResponseContext(ctx).RespondAsync(
            enabled
                ? $"✅ ロール通知チャンネルを #{ctx.Channel.Name} に設定しました。"
                : "✅ ロール解除通知を無効化しました。",
            true
        );
    }
}
