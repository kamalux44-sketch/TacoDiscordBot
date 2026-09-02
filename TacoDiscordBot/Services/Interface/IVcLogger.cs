using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace TacoDiscordBot.Services.Interface;

public interface IVcLogger
{
    bool IsConfiguredForGuild(ulong guildId);

    Task SetChannelAsync(ulong guildId, ulong channelId);

    Task RemoveChannelAsync(ulong guildId);

    Task HandleVoiceStateUpdated(DiscordClient client, VoiceStateUpdateEventArgs e);
}