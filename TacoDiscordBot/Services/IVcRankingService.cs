using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace TacoDiscordBot.Services;

public interface IVcRankingService
{
    Task HandleVoiceStateUpdated(DiscordClient client, VoiceStateUpdateEventArgs e);

    Task<DiscordEmbedBuilder> BuildRankingEmbedAsync(
        ulong guildId,
        string period,
        DiscordGuild guild,
        DiscordUser requestingUser
    );
}