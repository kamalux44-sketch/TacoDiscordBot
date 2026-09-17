using System.Collections.Concurrent;

namespace TacoDiscordBot.Services;

public sealed class SlotAnimationTracker
{
    private readonly ConcurrentDictionary<ulong, byte> _activeGuilds = new();

    public bool TryBegin(ulong guildId)
        => _activeGuilds.TryAdd(guildId, 0);

    public void End(ulong guildId)
        => _activeGuilds.TryRemove(guildId, out _);
}
