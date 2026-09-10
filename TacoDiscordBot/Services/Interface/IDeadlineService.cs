using System;
using System.Threading.Tasks;

namespace TacoDiscordBot.Services.Interface;

public interface IDeadlineService
{
    // 最新の募集へ締切日時を適用します。
    Task<bool> ApplyDeadlineToLatestSessionAsync(ulong userId, DateTime utcDeadline, string raw);
}