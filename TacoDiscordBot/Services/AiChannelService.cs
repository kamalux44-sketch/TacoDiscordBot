using System.Collections.Concurrent;
using System.Threading.Tasks;
using TacoDiscordBot.Repository;
using TacoDiscordBot.Services.Interface;

namespace TacoDiscordBot.Services;

public class AiChannelService : IAiChannelService
{
    // ギルドごとの AI 対象チャンネルを管理します。
    private readonly AiTalkRepository _repo;
    private readonly ConcurrentDictionary<ulong, ulong> _targets = new();

    public AiChannelService(AiTalkRepository repo = null)
    {
        _repo = repo;

        if (_repo == null)
            return;

        var all = _repo.LoadAllAsync().GetAwaiter().GetResult();

        foreach (var kv in all)
        {
            _targets[kv.Key] = kv.Value;
        }
    }

    public bool IsConfigured => _targets.Count > 0;

    // ギルドごとの設定チャンネルと受信チャンネルが一致するか確認します。
    public bool IsTargetChannel(ulong guildId, ulong channelId)
    {
        return _targets.TryGetValue(guildId, out var targetChannelId)
            && targetChannelId == channelId;
    }

    public async Task SetChannelAsync(ulong guildId, ulong channelId)
    {
        // DB が利用可能な場合は設定を永続化してからメモリ上の設定を更新します。
        if (_repo != null)
        {
            await _repo.SetTargetAsync(guildId, channelId);
        }

        _targets[guildId] = channelId;
    }

    public async Task RemoveChannelAsync(ulong guildId)
    {
        // 永続化された設定とメモリ上の設定を同時に削除します。
        if (_repo != null)
        {
            await _repo.RemoveTargetAsync(guildId);
        }

        _targets.TryRemove(guildId, out _);
    }
}
