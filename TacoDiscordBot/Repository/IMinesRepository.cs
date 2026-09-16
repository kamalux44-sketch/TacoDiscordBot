using System.Threading.Tasks;
using TacoDiscordBot.Models;

namespace TacoDiscordBot.Repository;

public interface IMinesRepository
{
    Task<bool> TryCreateAsync(MinesGame game);
    Task<MinesGame> GetAsync(ulong guildId, ulong userId);
    Task SaveAsync(MinesGame game);
    Task DeleteAsync(ulong guildId, ulong userId);
}
