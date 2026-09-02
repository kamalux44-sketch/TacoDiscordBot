using System;
using System.Threading.Tasks;

namespace TacoDiscordBot.Services;

public interface IDeadlineOwner
{
    Task<bool> ApplyDeadlineToLatestSessionAsync(ulong userId, DateTime utcDeadline, string raw);
}