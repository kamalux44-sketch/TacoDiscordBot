using System;
using System.Threading.Tasks;

namespace TacoDiscordBot.Services.Interface;

public interface IDeadlineOwner
{
    Task<bool> ApplyDeadlineToLatestSessionAsync(ulong userId, DateTime utcDeadline, string raw);
}