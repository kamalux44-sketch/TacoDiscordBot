using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.EventArgs;
using DSharpPlus.Entities;
using TacoDiscordBot.Repository;
using TacoDiscordBot.Util;


namespace TacoDiscordBot.Services;

public class VcLogger
{
    // per-guild targets are stored in DB if available. Fallback to legacy single-channel env var.
    private readonly VcLogRepository _repo;
    private readonly VcRankingRepository _rankingRepo;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, ulong> _targets = new();
    // legacy fallback
    private ulong _legacyChannelId; // 0=未設定
    private bool _legacyEnabled;
    // in-memory map of open sessions: key = "{guildId}:{userId}" -> (dbId, joinedAtUtc, channelId)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long dbId, DateTime joinedAtUtc, ulong channelId)> _openSessions = new();

    /// <summary>
    /// コンストラクタ。環境変数からチャンネルIDを読み取り、初期状態を設定します。
    /// </summary>
    // DI 対応コンストラクタ。リポジトリは null 可。
    public VcLogger(VcLogRepository repo, VcRankingRepository rankingRepo)
    {
        _legacyChannelId = 0;
        _legacyEnabled = false;

        _repo = repo;
        _rankingRepo = rankingRepo;

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

        LoadFromEnv();
    }

    // 既存の呼び出し互換のための引数無しコンストラクタ。
    public VcLogger() : this(CreateFromEnvOrNull(), CreateRankingFromEnvOrNull()) { }

    private static VcLogRepository CreateFromEnvOrNull()
    {
        try
        {
            var host = Environment.GetEnvironmentVariable("PGHOST");
            if (string.IsNullOrWhiteSpace(host))
                return null;

            var port = Environment.GetEnvironmentVariable("PGPORT") ?? Strings.DefaultDBPPort;
            var db = Environment.GetEnvironmentVariable("PGDATABASE") ?? Strings.DefaultDBName;
            var user = Environment.GetEnvironmentVariable("PGUSER");
            var pass = Environment.GetEnvironmentVariable("PGPASSWORD");
            var ssl = Environment.GetEnvironmentVariable("PGSSLMODE");

            var parts = new System.Collections.Generic.List<string>
            {
                $"Host={host}",
                $"Port={port}",
                $"Database={db}"
            };
            if (!string.IsNullOrWhiteSpace(user)) parts.Add($"Username={user}");
            if (!string.IsNullOrWhiteSpace(pass)) parts.Add($"Password={pass}");
            if (!string.IsNullOrWhiteSpace(ssl)) parts.Add($"SslMode={ssl}");

            var conn = string.Join(";", parts);
            var baseRepo = new Repository.BaseRepository(conn, s => Console.WriteLine($"[DB] {s}"));
            if (!baseRepo.IsProviderAvailable()) return null;
            return new VcLogRepository(baseRepo);
        }
        catch
        {
            return null;
        }
    }

    private static VcRankingRepository CreateRankingFromEnvOrNull()
    {
        try
        {
            var host = Environment.GetEnvironmentVariable("PGHOST");
            if (string.IsNullOrWhiteSpace(host))
                return null;

            var port = Environment.GetEnvironmentVariable("PGPORT") ?? Strings.DefaultDBPPort;
            var db = Environment.GetEnvironmentVariable("PGDATABASE") ?? Strings.DefaultDBName;
            var user = Environment.GetEnvironmentVariable("PGUSER");
            var pass = Environment.GetEnvironmentVariable("PGPASSWORD");
            var ssl = Environment.GetEnvironmentVariable("PGSSLMODE");

            var parts = new System.Collections.Generic.List<string>
            {
                $"Host={host}",
                $"Port={port}",
                $"Database={db}"
            };
            if (!string.IsNullOrWhiteSpace(user)) parts.Add($"Username={user}");
            if (!string.IsNullOrWhiteSpace(pass)) parts.Add($"Password={pass}");
            if (!string.IsNullOrWhiteSpace(ssl)) parts.Add($"SslMode={ssl}");

            var conn = string.Join(";", parts);
            var baseRepo = new Repository.BaseRepository(conn, s => Console.WriteLine($"[DB] {s}"));
            if (!baseRepo.IsProviderAvailable()) return null;
            return new VcRankingRepository(baseRepo);
        }
        catch
        {
            return null;
        }
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
                Logger.Info($"VcLogger: 環境変数からレガシーチャンネルを読み込み channel={cid}");
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
                Logger.Info($"VcLogger: ToggleChannel - removed guild={guildId}");
                return false;
            }
            else
            {
                // set to nothing: caller should call SetChannel to set channel
                Logger.Info($"VcLogger: ToggleChannel - will enable for guild={guildId}");
                return true;
            }
        }

        _legacyEnabled = !_legacyEnabled;
        Logger.Info($"VcLogger: ToggleChannel - legacyEnabled={_legacyEnabled}");
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
            Logger.Info($"VcLogger: SetChannelAsync guild={guildId} channel={channelId}");
            await _repo.SetTargetAsync(guildId, channelId);
            _targets[guildId] = channelId;
            return;
        }

        // fallback: set legacy channel
        _legacyChannelId = channelId;
        _legacyEnabled = true;
        Logger.Info($"VcLogger: SetChannelAsync - legacy channel set={channelId}");
    }

    public async Task RemoveChannelAsync(ulong guildId)
    {
        if (_repo != null)
        {
            Logger.Info($"VcLogger: RemoveChannelAsync guild={guildId}");
            await _repo.RemoveTargetAsync(guildId);
            _targets.TryRemove(guildId, out _);
            return;
        }
        _legacyChannelId = 0;
        _legacyEnabled = false;
        Logger.Info("VcLogger: RemoveChannelAsync - legacy channel disabled");
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
                // persist session start をログ出力しつつ DB に保存を試みる
                try
                {
                    if (_rankingRepo != null)
                    {
                        Logger.Info($"VcLogger: ユーザー入室を記録 guild={e.Guild.Id} user={e.User.Id} channel={after.Id}");
                        var id = _rankingRepo.CreateVcSessionAsync(e.Guild.Id, e.User.Id, after.Id, DateTime.UtcNow).GetAwaiter().GetResult();
                        var key = $"{e.Guild.Id}:{e.User.Id}";
                        _openSessions[key] = (id, DateTime.UtcNow, after.Id);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "VcLogger: セッション開始の永続化に失敗");
                }
            }
            else if (before != null && after == null)
            {
                // ユーザーが VC から退室した場合のログ
                text = string.Format(Strings.VcLogLeaveFmt, e.User.Mention, before.Name, DateTime.Now.ToString(Strings.DateTimeFormat));
                // persist session end をログ出力しつつ DB を更新
                try
                {
                    var key = $"{e.Guild.Id}:{e.User.Id}";
                    if (_openSessions.TryRemove(key, out var v))
                    {
                        var dur = (long)(DateTime.UtcNow - v.joinedAtUtc).TotalSeconds;
                        Logger.Info($"VcLogger: ユーザー退室を記録 id={v.dbId} duration={dur}");
                        _rankingRepo?.CloseVcSessionAsync(v.dbId, DateTime.UtcNow, dur).GetAwaiter().GetResult();
                    }
                    else
                    {
                        // fallback: try close latest session
                        Logger.Info($"VcLogger: Closing latest session for guild={e.Guild.Id} user={e.User.Id}");
                        _rankingRepo?.CloseLatestSessionForUserAsync(e.Guild.Id, e.User.Id, DateTime.UtcNow).GetAwaiter().GetResult();
                    }
                }
                catch
                {
                    // ignore
                }
            }
            else if (before != null && after != null && before.Id != after.Id)
            {
                // チャンネル移動が発生した場合のログ
                text = string.Format(Strings.VcLogMoveFmt, e.User.Mention, before.Name, after.Name, DateTime.Now.ToString(Strings.DateTimeFormat));
                // persist move: close previous session and create new one
                try
                {
                    var key = $"{e.Guild.Id}:{e.User.Id}";
                    if (_openSessions.TryRemove(key, out var v))
                    {
                        var dur = (long)(DateTime.UtcNow - v.joinedAtUtc).TotalSeconds;
                        _rankingRepo?.CloseVcSessionAsync(v.dbId, DateTime.UtcNow, dur).GetAwaiter().GetResult();
                    }
                    else
                    {
                        _rankingRepo?.CloseLatestSessionForUserAsync(e.Guild.Id, e.User.Id, DateTime.UtcNow).GetAwaiter().GetResult();
                    }

                    if (_rankingRepo != null)
                    {
                        var id = _rankingRepo.CreateVcSessionAsync(e.Guild.Id, e.User.Id, after.Id, DateTime.UtcNow).GetAwaiter().GetResult();
                        _openSessions[key] = (id, DateTime.UtcNow, after.Id);
                    }
                }
                catch
                {
                    // ignore
                }
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
                    title = Strings.VcEnterTitle;
                }
                else if (before != null && after == null)
                {
                    color = DiscordColor.Red;
                    title = Strings.VcLeaveTitle;
                }
                else
                {
                    color = DiscordColor.Yellow;
                    title = Strings.VcMoveTitle;
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

