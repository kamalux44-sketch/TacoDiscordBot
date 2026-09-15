using System.Collections.Generic;
using System.Threading.Tasks;
using TacoDiscordBot.Models;

namespace TacoDiscordBot.Services.Interface;

public interface ICoinService
{
    Task<long> GetBalanceAsync(ulong userId);
    Task<long> AddCoinsAsync(ulong userId, long amount);
    Task<long> RemoveCoinsAsync(ulong userId, long amount);
    Task<bool> CanAffordAsync(ulong userId, long amount);
    Task<IReadOnlyList<UserData>> GetRankingAsync();
}
