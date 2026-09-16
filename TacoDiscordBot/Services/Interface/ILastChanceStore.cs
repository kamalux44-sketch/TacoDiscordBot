using System.Threading.Tasks;

namespace TacoDiscordBot.Services.Interface;

public interface ILastChanceStore
{
    Task<bool> TryStartAsync(ulong guildId, ulong userId);

    Task<long?> CompleteAsync(ulong guildId, ulong userId, long reward);
}
