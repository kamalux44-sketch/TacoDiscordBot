using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.EventArgs;
using DSharpPlus.Entities;
using TacoDiscordBot.Repository;


namespace TacoDiscordBot.Services;

public class VcLogger
{
    // per-guild targets are stored in DB if available. Fallback to legacy single-channel env var.
    private readonly VcLogRepository _repo;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, ulong> _targets = new();
    // legacy fallback
    private ulong _legacyChannelId; // 0=未設定
    private bool _legacyEnabled;

    /// <summary>
    /// コンストラクタ。環境変数からチャンネルIDを読み取り、初期状態を設定します。
    /// </summary>
    public VcLogger()
    {
        _legacyChannelId = 0;
        _legacyEnabled = false;

        // Try to create repository from env. If exists, load targets into cache.
        _repo = VcLogRepository.TryCreateFromEnv();
        if (_repo != null)
        {
            try
            {
                var all = _repo.LoadAllAsync().GetAwaiter().GetResult();
                foreach (var kv in all)
                    _targets[kv.Key] = kv.Value;
            }
            catch
            {
                // ignore
            }
        }

        // legacy env var
        LoadFromEnv();
    }

    /// <summary>
    /// チャンネルが設定されているかを示します。
    /// </summary>
    // Indicates if any configuration exists (repo or legacy)
    public bool IsConfigured => _repo != null || _legacyChannelId != 0;

    public bool IsConfiguredForGuild(ulong guildId)
    {
        if (_repo != null)
            return _targets.ContainsKey(guildId);
        return _legacyChannelId != 0;
    }

    /// <summary>
    /// 環境変数から単一チャンネルIDを読み込みます。
    /// 環境変数が無い場合は未設定のままになります。
    /// </summary>
    private void LoadFromEnv()
    {
        try
        {
            var raw = Environment.GetEnvironmentVariable(Strings.EnvVcLogChannel);
            if (string.IsNullOrWhiteSpace(raw)) return;
            if (ulong.TryParse(raw.Trim(), out var cid))
            {
                _legacyChannelId = cid;
                _legacyEnabled = true; // 起動時は有効にする
            }
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// /vclog コマンドなどから呼ばれて、VC ログ機能のオン/オフを切り替えます。
    /// 戻り値は現在の有効状態です。
    /// </summary>
    // Toggle per-guild target when repo available; otherwise toggle legacy global channel.
    public bool ToggleChannel(ulong guildId)
    {
        if (_repo != null)
        {
            if (_targets.ContainsKey(guildId))
            {
                // remove
                try { _repo.RemoveTargetAsync(guildId).GetAwaiter().GetResult(); } catch { }
                _targets.TryRemove(guildId, out _);
                return false;
            }
            else
            {
                // set to nothing: caller should call SetChannel to set channel
                return true;
            }
        }

        _legacyEnabled = !_legacyEnabled;
        return _legacyEnabled;
    }

    /// <summary>
    /// 環境変数がない場合に、現在のチャンネルを VC ログ先として設定するために使います（メモリのみ）。
    /// </summary>
    public void SetChannel(ulong channelId)
    {
        // legacy fallback: set global channel
        _legacyChannelId = channelId;
        _legacyEnabled = true;
    }

    // Set per-guild target (persist if repo available)
    public async Task SetChannelAsync(ulong guildId, ulong channelId)
    {
        if (_repo != null)
        {
            await _repo.SetTargetAsync(guildId, channelId);
            _targets[guildId] = channelId;
            return;
        }

        // fallback: set legacy channel
        _legacyChannelId = channelId;
        _legacyEnabled = true;
    }

    public async Task RemoveChannelAsync(ulong guildId)
    {
        if (_repo != null)
        {
            await _repo.RemoveTargetAsync(guildId);
            _targets.TryRemove(guildId, out _);
            return;
        }

        _legacyChannelId = 0;
        _legacyEnabled = false;
    }

    public async Task HandleVoiceStateUpdated(DiscordClient client, VoiceStateUpdateEventArgs e)
    {
        try
        {
            if (e.Guild == null) return;

            // Determine channel to log to
            ulong targetChannel = 0;
            if (_repo != null)
            {
                _targets.TryGetValue(e.Guild.Id, out targetChannel);
            }
            else
            {
                if (_legacyEnabled && _legacyChannelId != 0)
                    targetChannel = _legacyChannelId;
            }

            if (targetChannel == 0) return;

            var before = e.Before?.Channel;
            var after = e.After?.Channel;
            string text = null;
            if (before == null && after != null)
            {
                // ユーザーが VC に入室した場合のログ文字列を作成します。
                // 出力に "誰が / どこに / いつ" が分かるようにする
                text = string.Format(Strings.VcLogJoinFmt, e.User.Mention, after.Name, DateTime.Now.ToString(Strings.DateTimeFormat));
            }
            else if (before != null && after == null)
            {
                // ユーザーが VC から退室した場合のログ
                text = string.Format(Strings.VcLogLeaveFmt, e.User.Mention, before.Name, DateTime.Now.ToString(Strings.DateTimeFormat));
            }
            else if (before != null && after != null && before.Id != after.Id)
            {
                // チャンネル移動が発生した場合のログ
                text = string.Format(Strings.VcLogMoveFmt, e.User.Mention, before.Name, after.Name, DateTime.Now.ToString(Strings.DateTimeFormat));
            }

            if (text == null) return;

            try
            {
                var ch = await client.GetChannelAsync(targetChannel);
                if (ch == null) return;

                // Embed with colored side bar depending on event type
                DiscordColor color;
                string title;
                if (before == null && after != null)
                {
                    color = DiscordColor.Green;
                    title = "VC 入室";
                }
                else if (before != null && after == null)
                {
                    color = DiscordColor.Red;
                    title = "VC 退室";
                }
                else
                {
                    color = DiscordColor.Yellow;
                    title = "VC 移動";
                }

                var embed = new DiscordEmbedBuilder()
                    .WithTitle(title)
                    .WithDescription(text)
                    .WithColor(color)
                    .WithTimestamp(DateTime.UtcNow);

                await ch.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(embed));
            }
            catch
            {
                // ignore send errors
            }
        }
        catch
        {
            // swallow
        }
    }
}
