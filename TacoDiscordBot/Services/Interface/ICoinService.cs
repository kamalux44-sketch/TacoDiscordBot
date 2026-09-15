using System.Collections.Generic;
using System.Threading.Tasks;
using TacoDiscordBot.Models;

namespace TacoDiscordBot.Services.Interface;

public interface ICoinService
{
    Task<long> GetBalanceAsync(ulong guildId, ulong userId);
    Task<long> AddCoinsAsync(ulong guildId, ulong userId, long amount);
    Task<long> RemoveCoinsAsync(ulong guildId, ulong userId, long amount);
    Task<bool> CanAffordAsync(ulong guildId, ulong userId, long amount);
    Task<IReadOnlyList<UserData>> GetRankingAsync(ulong guildId);
}
