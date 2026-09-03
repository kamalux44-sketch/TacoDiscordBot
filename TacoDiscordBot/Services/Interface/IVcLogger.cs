using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace TacoDiscordBot.Services.Interface;

public interface IVcLogger
{
    // ギルドに VC ログが設定されているか判定します。
    bool IsConfiguredForGuild(ulong guildId);

    // VC ログの出力先チャンネルを設定します。
    Task SetChannelAsync(ulong guildId, ulong channelId);

    // VC ログの出力先チャンネルを削除します。
    Task RemoveChannelAsync(ulong guildId);

    // Discord のボイス状態更新イベントを処理します。
    Task HandleVoiceStateUpdated(DiscordClient client, VoiceStateUpdateEventArgs e);
}