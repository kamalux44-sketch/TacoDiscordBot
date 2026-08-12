using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.EventArgs;
using DSharpPlus.Entities;


namespace TacoDiscordBot.Services;

public class VcLogger
{
    // 単一チャンネル運用を想定して、環境変数からチャンネルIDを読み込みます。
    // 実行時はメモリ上のフラグでオン/オフを切り替えます（永続化しません）。
    private ulong _channelId; // 0=未設定
    private bool _enabled;

    /// <summary>
    /// コンストラクタ。環境変数からチャンネルIDを読み取り、初期状態を設定します。
    /// </summary>
    public VcLogger()
    {
        _channelId = 0;
        _enabled = false;
        LoadFromEnv();
    }

    /// <summary>
    /// チャンネルが設定されているかを示します。
    /// </summary>
    public bool IsConfigured => _channelId != 0;

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
                _channelId = cid;
                _enabled = true; // 起動時は有効にする
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
    public bool ToggleChannel()
    {
        _enabled = !_enabled;
        return _enabled;
    }

    /// <summary>
    /// 環境変数がない場合に、現在のチャンネルを VC ログ先として設定するために使います（メモリのみ）。
    /// </summary>
    public void SetChannel(ulong channelId)
    {
        _channelId = channelId;
        _enabled = true;
    }

    public async Task HandleVoiceStateUpdated(DiscordClient client, VoiceStateUpdateEventArgs e)
    {
        try
        {
            if (e.Guild == null) return;
            if (!_enabled || _channelId == 0) return;

            var before = e.Before?.Channel;
            var after = e.After?.Channel;
            string text = null;
            if (before == null && after != null)
            {
                // ユーザーが VC に入室した場合のログ文字列を作成します。
                text = string.Format(Strings.VcLogJoinFmt, e.User.Mention, DateTime.Now.ToString(Strings.DateTimeFormat));
            }
            else if (before != null && after == null)
            {
                // ユーザーが VC から退室した場合のログ
                text = string.Format(Strings.VcLogLeaveFmt, e.User.Mention, DateTime.Now.ToString(Strings.DateTimeFormat));
            }
            else if (before != null && after != null && before.Id != after.Id)
            {
                // チャンネル移動が発生した場合のログ
                text = string.Format(Strings.VcLogMoveFmt, e.User.Mention, before.Name, after.Name, DateTime.Now.ToString(Strings.DateTimeFormat));
            }

            if (text == null) return;

            try
            {
                var ch = await client.GetChannelAsync(_channelId);
                if (ch != null)
                    await ch.SendMessageAsync(text);
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
