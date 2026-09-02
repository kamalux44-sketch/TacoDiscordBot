using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using TacoDiscordBot.Services.Interface;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Services;

public class DeadlineService
{
    private readonly IDeadlineOwner _owner;
    private readonly ConcurrentDictionary<ulong, DeadlineSelection> _selections = new();

    private sealed class DeadlineSelection
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int Day { get; set; }
        public int Hour { get; set; }
        public int Minute { get; set; }
    }

    public DeadlineService(IDeadlineOwner owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

        Logger.Info("DeadlineService: 初期化完了");
    }

    /// <summary>
    /// deadline_* 系のコンポーネントインタラクションを処理します。
    /// 処理対象であれば true、対象外であれば false を返します。
    /// </summary>
    public async Task<bool> HandleInteractionAsync(ComponentInteractionCreateEventArgs e)
    {
        if (e == null)
            return false;

        var id = e.Id ?? e.Interaction?.Data?.CustomId;

        Logger.Info(
            "DeadlineService: interaction id={InteractionId} user={UserId}",
            id,
            e.User?.Id
        );

        if (string.IsNullOrWhiteSpace(id) || !id.StartsWith("deadline_", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = id.Split(':', 2);

        if (parts.Length != 2)
        {
            await SafeCreateResponseAsync(e, "締め切り操作の情報が正しくありません。");

            return true;
        }

        var action = parts[0];

        if (!ulong.TryParse(parts[1], out var ownerId))
        {
            await SafeCreateResponseAsync(e, "締め切り操作のユーザー情報が正しくありません。");

            return true;
        }

        // 締め切り UI は実行した本人だけが操作可能
        if (ownerId != e.User.Id)
        {
            await SafeCreateResponseAsync(e, "この UI は実行した本人のみ操作できます。");

            return true;
        }

        try
        {
            switch (action)
            {
                case "deadline_date":
                    return await HandleDateAsync(e);

                case "deadline_hour":
                    return await HandleHourAsync(e);

                case "deadline_min":
                    return await HandleMinuteAsync(e);

                case "deadline_confirm":
                    return await HandleConfirmAsync(e);

                default:
                    Logger.Info("DeadlineService: 未対応 action={Action}", action);

                    return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(
                ex,
                "DeadlineService: interaction handling failed action={Action}",
                action
            );

            await SafeCreateResponseAsync(e, "締め切り処理中にエラーが発生しました。");

            return true;
        }
    }

    /// <summary>
    /// 日付選択を処理します。
    /// </summary>
    private async Task<bool> HandleDateAsync(ComponentInteractionCreateEventArgs e)
    {
        var value = e.Interaction?.Data?.Values?.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(value))
        {
            await SafeCreateResponseAsync(e, "日付が選択されていません。");

            return true;
        }

        if (
            !DateTime.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date
            )
        )
        {
            await SafeCreateResponseAsync(e, "日付の形式が正しくありません。");

            return true;
        }

        var selection = GetOrCreateSelection(e.User.Id);

        selection.Year = date.Year;
        selection.Month = date.Month;
        selection.Day = date.Day;

        Logger.Info("DeadlineService: date selected user={UserId} date={Date}", e.User.Id, value);

        await AcknowledgeComponentAsync(e);

        return true;
    }

    /// <summary>
    /// 時刻の「時」を処理します。
    /// </summary>
    private async Task<bool> HandleHourAsync(ComponentInteractionCreateEventArgs e)
    {
        var value = e.Interaction?.Data?.Values?.FirstOrDefault();

        if (
            !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour)
            || hour < 0
            || hour > 23
        )
        {
            await SafeCreateResponseAsync(e, "時の選択が無効です。");

            return true;
        }

        var selection = GetOrCreateSelection(e.User.Id);

        selection.Hour = hour;

        Logger.Info("DeadlineService: hour selected user={UserId} hour={Hour}", e.User.Id, hour);

        await AcknowledgeComponentAsync(e);

        return true;
    }

    /// <summary>
    /// 時刻の「分」を処理します。
    /// </summary>
    private async Task<bool> HandleMinuteAsync(ComponentInteractionCreateEventArgs e)
    {
        var value = e.Interaction?.Data?.Values?.FirstOrDefault();

        if (
            !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minute)
            || minute < 0
            || minute > 59
        )
        {
            await SafeCreateResponseAsync(e, "分の選択が無効です。");

            return true;
        }

        var selection = GetOrCreateSelection(e.User.Id);

        selection.Minute = minute;

        Logger.Info(
            "DeadlineService: minute selected user={UserId} minute={Minute}",
            e.User.Id,
            minute
        );

        await AcknowledgeComponentAsync(e);

        return true;
    }

    /// <summary>
    /// 締め切り確定を処理します。
    /// 入力値は JST として扱い、保存時には UTC に変換します。
    /// </summary>
    private async Task<bool> HandleConfirmAsync(ComponentInteractionCreateEventArgs e)
    {
        if (!_selections.TryGetValue(e.User.Id, out var selection))
        {
            await SafeCreateResponseAsync(
                e,
                "締め切りが選択されていません。日付・時・分を選択してください。"
            );

            return true;
        }

        DateTime localDateTime;

        try
        {
            localDateTime = new DateTime(
                selection.Year,
                selection.Month,
                selection.Day,
                selection.Hour,
                selection.Minute,
                0,
                DateTimeKind.Unspecified
            );
        }
        catch (ArgumentOutOfRangeException)
        {
            await SafeCreateResponseAsync(e, "選択された締め切り日時が正しくありません。");

            return true;
        }

        var jst = GetJapanTimeZone();

        DateTime utcDeadline;

        try
        {
            utcDeadline = TimeZoneInfo.ConvertTimeToUtc(localDateTime, jst);
        }
        catch (ArgumentException ex)
        {
            Logger.Error(ex, "DeadlineService: JST -> UTC 変換失敗");

            await SafeCreateResponseAsync(e, "締め切り日時の変換に失敗しました。");

            return true;
        }

        // Discord 上で表示するための JST 文字列
        var raw = localDateTime.ToString(Strings.DateTimeFormat, CultureInfo.InvariantCulture);

        Logger.Info(
            "DeadlineService: confirm user={UserId} jst={JstDeadline:o} utc={UtcDeadline:o}",
            e.User.Id,
            localDateTime,
            utcDeadline
        );

        // 先に締め切りを募集へ適用
        var applied = await _owner.ApplyDeadlineToLatestSessionAsync(e.User.Id, utcDeadline, raw);

        if (!applied)
        {
            _selections.TryRemove(e.User.Id, out _);

            await SafeCreateResponseAsync(e, "直近の募集が見つかりませんでした。");

            return true;
        }

        // 成功時のみ選択状態を破棄
        _selections.TryRemove(e.User.Id, out _);

        await SafeCreateResponseAsync(e, $"締め切りを {raw} に設定しました。");

        return true;
    }

    /// <summary>
    /// ユーザーごとの選択状態を取得します。
    /// 未作成の場合は現在の JST 日付を初期値として作成します。
    /// </summary>
    private DeadlineSelection GetOrCreateSelection(ulong userId)
    {
        return _selections.GetOrAdd(
            userId,
            _ =>
            {
                var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, GetJapanTimeZone());

                return new DeadlineSelection
                {
                    Year = now.Year,
                    Month = now.Month,
                    Day = now.Day,
                    Hour = 0,
                    Minute = 0,
                };
            }
        );
    }

    /// <summary>
    /// SelectMenu などのコンポーネント操作を
    /// メッセージ更新なしで ACK します。
    /// </summary>
    private async Task AcknowledgeComponentAsync(ComponentInteractionCreateEventArgs e)
    {
        try
        {
            await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
        }
        catch (Exception ex)
        {
            // 既に ACK 済みの場合などはここに入る可能性があります。
            // 二重応答を試みず、ログだけ残します。
            Logger.Error(ex, "DeadlineService: component ACK 失敗");
        }
    }

    /// <summary>
    /// Interaction の初回レスポンスを安全に返します。
    /// </summary>
    private async Task SafeCreateResponseAsync(
        ComponentInteractionCreateEventArgs e,
        string content
    )
    {
        try
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent(content).AsEphemeral(true)
            );
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "DeadlineService: response 作成失敗");
        }
    }

    /// <summary>
    /// 日本標準時の TimeZoneInfo を取得します。
    /// Windows / Linux の両方を考慮します。
    /// </summary>
    private static TimeZoneInfo GetJapanTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
