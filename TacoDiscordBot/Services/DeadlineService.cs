using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Services;

public class DeadlineService
{
    private readonly BoManager _owner;
    private readonly ConcurrentDictionary<ulong, DeadlineSelection> _selections = new();

    private class DeadlineSelection
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int Day { get; set; }
        public int Hour { get; set; }
        public int Minute { get; set; }
    }

    public DeadlineService(BoManager owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>
    /// コンポーネントインタラクションを処理します。
    /// deadline_* 系の customId をハンドリングし、必要に応じて募集へ締め切りを適用します。
    /// 成功または処理済みの場合は true を返します。
    /// </summary>
    public async Task<bool> HandleInteractionAsync(ComponentInteractionCreateEventArgs e)
    {
        var id = e.Id ?? e?.Interaction?.Data?.CustomId;
        if (string.IsNullOrEmpty(id))
            return false;

        if (!id.StartsWith("deadline_"))
            return false;

        var parts = id.Split(':', 2);
        if (parts.Length != 2)
            return false;

        var action = parts[0];
        if (!ulong.TryParse(parts[1], out var ownerId))
            return false;

        // この UI は実行ユーザー専用
        if (ownerId != e.User.Id)
        {
            await SafeCreateResponseAsync(e, "この UI は実行した本人のみ操作できます。");
            return true;
        }

        var sel = _selections.GetOrAdd(e.User.Id, _ =>
        {
            var n = DateTime.Now;
            return new DeadlineSelection
            {
                Year = n.Year,
                Month = n.Month,
                Day = n.Day,
                Hour = 0,
                Minute = 0
            };
        });

        try
        {
            if (action == "deadline_date")
            {
                var val = e?.Interaction?.Data?.Values?.FirstOrDefault();
                if (string.IsNullOrEmpty(val))
                {
                    await SafeCreateResponseAsync(e, "日付が選択されていません。");
                    return true;
                }

                var dt = DateTime.ParseExact(val, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                sel.Year = dt.Year;
                sel.Month = dt.Month;
                sel.Day = dt.Day;

                await SafeCreateFollowupAsync(e, $"日付を {dt.ToString("yyyy/MM/dd")} に設定しました。");
                return true;
            }

            if (action == "deadline_hour")
            {
                var val = e?.Interaction?.Data?.Values?.FirstOrDefault();
                if (int.TryParse(val, out var h))
                {
                    sel.Hour = h;
                    await SafeCreateFollowupAsync(e, $"時を {h:D2} に設定しました。");
                }
                else
                {
                    await SafeCreateResponseAsync(e, "時の選択が無効です。");
                }
                return true;
            }

            if (action == "deadline_min")
            {
                var val = e?.Interaction?.Data?.Values?.FirstOrDefault();
                if (int.TryParse(val, out var m))
                {
                    sel.Minute = m;
                    await SafeCreateFollowupAsync(e, $"分を {m:D2} に設定しました。");
                }
                else
                {
                    await SafeCreateResponseAsync(e, "分の選択が無効です。");
                }
                return true;
            }

            if (action == "deadline_confirm")
            {
                if (!_selections.TryGetValue(e.User.Id, out var s))
                {
                    await SafeCreateResponseAsync(e, "締め切りが選択されていません。日付・時・分を選択してください。");
                    return true;
                }

                var dt = new DateTime(s.Year, s.Month, s.Day, s.Hour, s.Minute, 0);
                var jst = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
                var unspecified = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
                var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, jst);

                var raw = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), jst).ToString(Strings.DateTimeFormat);

                // BoManager に適用を依頼
                var applied = await _owner.ApplyDeadlineToLatestSessionAsync(e.User.Id, utc, raw);
                if (!applied)
                {
                    await SafeCreateResponseAsync(e, "直近の募集が見つかりませんでした。");
                    return true;
                }

                await SafeCreateResponseAsync(e, "締め切りを設定しました。");
                _selections.TryRemove(e.User.Id, out _);
                return true;
            }

            if (action == "deadline_cancel")
            {
                _selections.TryRemove(e.User.Id, out _);
                await SafeCreateResponseAsync(e, "キャンセルしました。");
                return true;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "deadline component handling");
            await SafeCreateResponseAsync(e, "処理中にエラーが発生しました。");
            return true;
        }

        return false;
    }

    private async Task SafeCreateResponseAsync(ComponentInteractionCreateEventArgs e, string content)
    {
        try
        {
            await e.Interaction.CreateResponseAsync(DSharpPlus.InteractionResponseType.ChannelMessageWithSource,
                new DSharpPlus.Entities.DiscordInteractionResponseBuilder().WithContent(content).AsEphemeral(true));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "SafeCreateResponse failed");
        }
    }

    private async Task SafeCreateFollowupAsync(ComponentInteractionCreateEventArgs e, string content)
    {
        try
        {
            await e.Interaction.CreateFollowupMessageAsync(new DSharpPlus.Entities.DiscordFollowupMessageBuilder().WithContent(content).AsEphemeral(true));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "SafeCreateFollowup failed");
        }
    }
}
