using System.Threading.Tasks;

namespace TacoDiscordBot.Services.Interface;

public interface IAiChannelService
{
    // ギルドの AI 対象チャンネルを登録します。
    Task SetChannelAsync(ulong guildId, ulong channelId);

    // ギルドの AI 対象チャンネル登録を解除します。
    Task RemoveChannelAsync(ulong guildId);

    // 指定チャンネルが AI 対象として登録されているか判定します。
    bool IsTargetChannel(ulong guildId, ulong channelId);
}