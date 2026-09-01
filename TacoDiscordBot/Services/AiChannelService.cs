using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using TacoDiscordBot.Repository;

namespace TacoDiscordBot.Services;

public class AiChannelService
{
    private readonly AiTalkRepository _repo;
    private readonly ConcurrentDictionary<ulong, ulong> _targets = new();

    public AiChannelService(AiTalkRepository repo = null)
    {
        _repo = repo;

        if (_repo != null)
        {
            try
            {
                var all = _repo.LoadAllAsync().GetAwaiter().GetResult();
                foreach (var kv in all) _targets[kv.Key] = kv.Value;
            }
            catch
            {
                // ignore
            }
        }
    }

    public bool IsConfigured => _repo != null || _targets.Count > 0;

    public bool IsTargetChannel(ulong guildId, ulong channelId)
    {
        if (_repo != null) return _targets.TryGetValue(guildId, out var v) && v == channelId;
        return false;
    }

    public async Task SetChannelAsync(ulong guildId, ulong channelId)
    {
        if (_repo != null)
        {
            await _repo.SetTargetAsync(guildId, channelId);
            _targets[guildId] = channelId;
        }
        else
        {
            _targets[guildId] = channelId; // in-memory fallback
        }
    }
}
