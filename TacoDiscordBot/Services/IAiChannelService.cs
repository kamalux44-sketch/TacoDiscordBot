using System.Threading.Tasks;

namespace TacoDiscordBot.Services;

public interface IAiChannelService
{
    Task SetChannelAsync(ulong guildId, ulong channelId);

    Task RemoveChannelAsync(ulong guildId);

    bool IsTargetChannel(ulong guildId, ulong channelId);
}