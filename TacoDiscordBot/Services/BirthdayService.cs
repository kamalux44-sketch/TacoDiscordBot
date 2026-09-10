using System;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using TacoDiscordBot.Models;
using TacoDiscordBot.Repository;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Services;

public sealed class BirthdayService
{
    private const int MinimumYear = 1;
    private readonly DiscordClient _client;
    private readonly BirthdayRepository _repository;
    private readonly TimeZoneInfo _japanTimeZone;

    public BirthdayService(DiscordClient client, BirthdayRepository repository)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _japanTimeZone = GetJapanTimeZone();
    }

    public async Task<string?> RegisterAsync(ulong userId, int? year, int month, int day)
    {
        var validationError = Validate(year, month, day);
        if (validationError != null)
            return validationError;

        await _repository.UpsertAsync(userId, year, month, day);
        return null;
    }

    public Task SetChannelAsync(ulong guildId, ulong channelId)
    {
        return _repository.SetChannelAsync(guildId, channelId);
    }

    public void StartDailyPosting()
    {
        _ = Task.Run(RunDailyPostingAsync);
    }

    public static string? Validate(int? year, int month, int day)
    {
        if (year.HasValue && (year.Value < MinimumYear || year.Value > DateTime.UtcNow.Year))
            return "誕生年が正しくありません。";

        if (month is < 1 or > 12 || day is < 1 or > 31)
            return "誕生日は正しい月日を指定してください。";

        var validationYear = year ?? 2000;
        if (!DateTime.TryParseExact(
                $"{validationYear:D4}-{month:D2}-{day:D2}",
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out _))
            return "指定された月日に実在する日付ではありません。";

        return null;
    }

    private async Task RunDailyPostingAsync()
    {
        while (true)
        {
            try
            {
                var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _japanTimeZone);
                await PostBirthdaysAsync(now);
                await Task.Delay(GetDelayUntilNextDay(now));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "BirthdayService: 日次投稿に失敗しました");
                await Task.Delay(TimeSpan.FromMinutes(5));
            }
        }
    }

    private async Task PostBirthdaysAsync(DateTime japanNow)
    {
        var records = await _repository.GetByMonthAndDayAsync(japanNow.Month, japanNow.Day);
        var channelIds = await _repository.GetChannelIdsAsync();

        foreach (var channelId in channelIds)
        {
            var channel = await _client.GetChannelAsync(channelId);
            if (!channel.GuildId.HasValue)
                continue;

            foreach (var record in records)
            {
                if (!await _repository.TryRecordPostAsync(channel.GuildId.Value, record.UserId, japanNow.Year))
                    continue;

                var message = CreateMessage(record, japanNow.Year);
                try
                {
                    await channel.SendMessageAsync(message);
                }
                catch
                {
                    await _repository.RemovePostAsync(channel.GuildId.Value, record.UserId, japanNow.Year);
                    Logger.Error(new InvalidOperationException("誕生日メッセージの送信に失敗しました。"),
                        "BirthdayService: 投稿失敗 channel={ChannelId} user={UserId}", channelId, record.UserId);
                }
            }
        }
    }

    private static string CreateMessage(BirthdayRecord record, int currentYear)
    {
        var ageMessage = record.Year.HasValue ? $"{currentYear - record.Year.Value}歳の" : string.Empty;
        var additionalMessage = Strings.birthdayMessages[Random.Shared.Next(Strings.birthdayMessages.Length)];

        return $"<@{record.UserId}> {ageMessage}誕生日おめでとう！{additionalMessage}";
    }

    private static TimeSpan GetDelayUntilNextDay(DateTime japanNow)
    {
        var nextDay = japanNow.Date.AddDays(1);
        return nextDay - japanNow;
    }

    private static TimeZoneInfo GetJapanTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
        }
    }
}
