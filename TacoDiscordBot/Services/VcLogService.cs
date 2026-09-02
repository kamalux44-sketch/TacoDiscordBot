using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using TacoDiscordBot.Repository;
using TacoDiscordBot.Services.Interface;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Services;

public class VcLogService : IVcLogger
{
    // ギルドごとのターゲットは DB に保存されます。
    // 利用できない場合はレガシーな単一チャンネル環境変数を使用します。
    private readonly VcLogRepository _repo;
    private readonly ConcurrentDictionary<ulong, ulong> _targets = new();

    // レガシーフォールバック
    private ulong _legacyChannelId; // 0 = 未設定
    private bool _legacyEnabled;

    /// <summary>
    /// コンストラクタ。
    /// リポジトリからギルドごとの VC ログチャンネルを読み込みます。
    /// </summary>
    public VcLogService(VcLogRepository repo, VcRankingRepository rankingRepo)
    {
        _legacyChannelId = 0;
        _legacyEnabled = false;

        _repo = repo;

        // rankingRepo は意図的に保持しません。
        // ランキング永続化は VcRankingService が担当します。

        if (_repo != null)
        {
            try
            {
                var all = _repo.LoadAllAsync().GetAwaiter().GetResult();

                foreach (var kv in all)
                    _targets[kv.Key] = kv.Value;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "VcLogger: DB から VC ログ設定の読み込みに失敗");
            }
        }

        LoadFromEnv();
    }

    /// <summary>
    /// 既存の呼び出し互換のための引数無しコンストラクタ。
    /// </summary>
    public VcLogService()
        : this(CreateFromEnvOrNull(), CreateRankingFromEnvOrNull()) { }

    /// <summary>
    /// 環境変数から VcLogRepository を作成します。
    /// DB 設定が存在しない場合は null を返します。
    /// </summary>
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

            var parts = new List<string> { $"Host={host}", $"Port={port}", $"Database={db}" };

            if (!string.IsNullOrWhiteSpace(user))
                parts.Add($"Username={user}");

            if (!string.IsNullOrWhiteSpace(pass))
                parts.Add($"Password={pass}");

            if (!string.IsNullOrWhiteSpace(ssl))
                parts.Add($"SslMode={ssl}");

            var conn = string.Join(";", parts);

            var baseRepo = new Repository.BaseRepository(conn);

            if (!baseRepo.IsProviderAvailable())
                return null;

            return new VcLogRepository(baseRepo);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "VcLogger: VC ログリポジトリの作成に失敗");
            return null;
        }
    }

    /// <summary>
    /// 環境変数から VcRankingRepository を作成します。
    /// </summary>
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

            var parts = new List<string> { $"Host={host}", $"Port={port}", $"Database={db}" };

            if (!string.IsNullOrWhiteSpace(user))
                parts.Add($"Username={user}");

            if (!string.IsNullOrWhiteSpace(pass))
                parts.Add($"Password={pass}");

            if (!string.IsNullOrWhiteSpace(ssl))
                parts.Add($"SslMode={ssl}");

            var conn = string.Join(";", parts);

            var baseRepo = new Repository.BaseRepository(conn);

            if (!baseRepo.IsProviderAvailable())
                return null;

            return new VcRankingRepository(baseRepo);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "VcLogger: VC ランキングリポジトリの作成に失敗");
            return null;
        }
    }

    /// <summary>
    /// チャンネルが設定されているかを示します。
    /// </summary>
    public bool IsConfigured => _repo != null || (_legacyEnabled && _legacyChannelId != 0);

    /// <summary>
    /// 指定ギルドに VC ログチャンネルが設定されているかを示します。
    /// </summary>
    public bool IsConfiguredForGuild(ulong guildId)
    {
        if (_repo != null)
            return _targets.ContainsKey(guildId);

        return _legacyEnabled && _legacyChannelId != 0;
    }

    /// <summary>
    /// 環境変数から単一チャンネル ID を読み込みます。
    /// </summary>
    private void LoadFromEnv()
    {
        try
        {
            var raw = Environment.GetEnvironmentVariable(Strings.EnvVcLogChannel);

            if (string.IsNullOrWhiteSpace(raw))
                return;

            if (ulong.TryParse(raw.Trim(), out var cid))
            {
                _legacyChannelId = cid;
                _legacyEnabled = true;

                Logger.Info(
                    "VcLogger: 環境変数からレガシーチャンネルを読み込み channel={ChannelId}",
                    cid
                );
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "VcLogger: レガシーチャンネル設定の読み込みに失敗");
        }
    }

    /// <summary>
    /// /vclog コマンドなどから呼ばれて、
    /// VC ログ機能のオン / オフを切り替えます。
    /// 戻り値は現在の有効状態です。
    /// </summary>
    public bool ToggleChannel(ulong guildId)
    {
        if (_repo != null)
        {
            if (_targets.ContainsKey(guildId))
            {
                // 削除
                try
                {
                    _repo.RemoveTargetAsync(guildId).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Logger.Error(
                        ex,
                        "VcLogger: VC ログチャンネル設定の削除に失敗 guild={GuildId}",
                        guildId
                    );
                }

                _targets.TryRemove(guildId, out _);

                Logger.Info("VcLogger: ToggleChannel - removed guild={GuildId}", guildId);

                return false;
            }

            // DB を使用する場合、
            // 実際のチャンネル設定は SetChannelAsync で行います。
            Logger.Info("VcLogger: ToggleChannel - will enable for guild={GuildId}", guildId);

            return true;
        }

        _legacyEnabled = !_legacyEnabled;

        Logger.Info("VcLogger: ToggleChannel - legacyEnabled={LegacyEnabled}", _legacyEnabled);

        return _legacyEnabled;
    }

    /// <summary>
    /// レガシーフォールバック用。
    /// 現在のチャンネルを VC ログ先として設定します。
    /// </summary>
    public void SetChannel(ulong channelId)
    {
        _legacyChannelId = channelId;
        _legacyEnabled = true;
    }

    /// <summary>
    /// ギルド単位の VC ログターゲットを設定します。
    /// リポジトリが存在する場合は DB に永続化します。
    /// </summary>
    public async Task SetChannelAsync(ulong guildId, ulong channelId)
    {
        if (_repo != null)
        {
            Logger.Info(
                "VcLogger: SetChannelAsync guild={GuildId} channel={ChannelId}",
                guildId,
                channelId
            );

            await _repo.SetTargetAsync(guildId, channelId);

            _targets[guildId] = channelId;

            return;
        }

        // フォールバック
        _legacyChannelId = channelId;
        _legacyEnabled = true;

        Logger.Info("VcLogger: SetChannelAsync - legacy channel set={ChannelId}", channelId);
    }

    /// <summary>
    /// ギルドの VC ログチャンネル設定を削除します。
    /// </summary>
    public async Task RemoveChannelAsync(ulong guildId)
    {
        if (_repo != null)
        {
            Logger.Info("VcLogger: RemoveChannelAsync guild={GuildId}", guildId);

            await _repo.RemoveTargetAsync(guildId);

            _targets.TryRemove(guildId, out _);

            return;
        }

        _legacyChannelId = 0;
        _legacyEnabled = false;

        Logger.Info("VcLogger: RemoveChannelAsync - legacy channel disabled");
    }

    /// <summary>
    /// VC の入退室・移動イベントを処理します。
    /// </summary>
    public async Task HandleVoiceStateUpdated(DiscordClient client, VoiceStateUpdateEventArgs e)
    {
        try
        {
            if (e.Guild == null)
                return;

            var targetChannel = GetTargetChannelId(e.Guild.Id);
            var log = CreateVoiceLog(e);

            if (targetChannel == 0 || log == null)
                return;

            await SendVoiceLogAsync(client, e, targetChannel, log.Value);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "VcLogger: VC 状態更新の処理に失敗 guild={GuildId}", e.Guild?.Id);
        }
    }

    private ulong GetTargetChannelId(ulong guildId)
    {
        if (_repo != null)
        {
            _targets.TryGetValue(guildId, out var targetChannel);
            return targetChannel;
        }

        return _legacyEnabled ? _legacyChannelId : 0;
    }

    private (string Text, string Title, DiscordColor Color)? CreateVoiceLog(
        VoiceStateUpdateEventArgs e
    )
    {
        var before = e.Before?.Channel;
        var after = e.After?.Channel;
        var timestamp = DateTime.Now.ToString(Strings.DateTimeFormat);

        if (before == null && after != null)
        {
            return (
                string.Format(Strings.VcLogJoinFmt, e.User.Mention, after.Name, timestamp),
                Strings.VcEnterTitle,
                DiscordColor.Green
            );
        }

        if (before != null && after == null)
        {
            return (
                string.Format(Strings.VcLogLeaveFmt, e.User.Mention, before.Name, timestamp),
                Strings.VcLeaveTitle,
                DiscordColor.Red
            );
        }

        if (before != null && after != null && before.Id != after.Id)
        {
            return (
                string.Format(
                    Strings.VcLogMoveFmt,
                    e.User.Mention,
                    before.Name,
                    after.Name,
                    timestamp
                ),
                Strings.VcMoveTitle,
                DiscordColor.Yellow
            );
        }

        return null;
    }

    private async Task SendVoiceLogAsync(
        DiscordClient client,
        VoiceStateUpdateEventArgs e,
        ulong targetChannel,
        (string Text, string Title, DiscordColor Color) log
    )
    {
        try
        {
            var channel = await client.GetChannelAsync(targetChannel);

            if (channel == null)
                return;

            var embed = new DiscordEmbedBuilder()
                .WithTitle(log.Title)
                .WithDescription(log.Text)
                .WithColor(log.Color)
                .WithTimestamp(DateTime.UtcNow);

            await channel.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(embed));
        }
        catch (Exception ex)
        {
            Logger.Error(
                ex,
                "VcLogger: VC ログの送信に失敗 guild={GuildId} channel={ChannelId}",
                e.Guild.Id,
                targetChannel
            );
        }
    }
}
