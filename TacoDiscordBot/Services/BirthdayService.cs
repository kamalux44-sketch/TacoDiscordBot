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
        Logger.Info("BirthdayService: 誕生日登録開始 user={UserId} year={Year} month={Month} day={Day}",
            userId, year, month, day);

        var validationError = Validate(year, month, day);
        if (validationError != null)
        {
            Logger.Info("BirthdayService: 誕生日登録の入力検証失敗 user={UserId} reason={Reason}", userId, validationError);
            return validationError;
        }

        await _repository.UpsertAsync(userId, year, month, day);
        Logger.Info("BirthdayService: 誕生日登録完了 user={UserId}", userId);
        return null;
    }

    public Task SetChannelAsync(ulong guildId, ulong channelId)
    {
        Logger.Info("BirthdayService: 投稿先設定開始 guild={GuildId} channel={ChannelId}", guildId, channelId);
        return _repository.SetChannelAsync(guildId, channelId);
    }

    public void StartDailyPosting()
    {
        Logger.Info("BirthdayService: 日次投稿処理を開始します");
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
                Logger.Info("BirthdayService: 日次投稿処理実行 date={Date}", now.ToString("yyyy-MM-dd"));
                await PostBirthdaysAsync(now);
                var delay = GetDelayUntilNextDay(now);
                Logger.Info("BirthdayService: 次回実行まで待機 duration={Duration}", delay);
                await Task.Delay(delay);
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
        Logger.Info("BirthdayService: 投稿対象取得 date={Date} users={UserCount} channels={ChannelCount}",
            japanNow.ToString("yyyy-MM-dd"), records.Count, channelIds.Count);

        foreach (var channelId in channelIds)
        {
            var channel = await _client.GetChannelAsync(channelId);
            if (!channel.GuildId.HasValue)
            {
                Logger.Info("BirthdayService: ギルドに属さないチャンネルをスキップ channel={ChannelId}", channelId);
                continue;
            }

            foreach (var record in records)
            {
                if (!await _repository.TryRecordPostAsync(channel.GuildId.Value, record.UserId, japanNow.Year))
                {
                    Logger.Info("BirthdayService: 投稿済みのためスキップ guild={GuildId} user={UserId} year={Year}",
                        channel.GuildId.Value, record.UserId, japanNow.Year);
                    continue;
                }

                var message = CreateMessage(record, japanNow.Year);
                try
                {
                    await channel.SendMessageAsync(message);
                    Logger.Info("BirthdayService: 誕生日メッセージ送信完了 guild={GuildId} channel={ChannelId} user={UserId}",
                        channel.GuildId.Value, channelId, record.UserId);
                }
                catch (Exception ex)
                {
                    await _repository.RemovePostAsync(channel.GuildId.Value, record.UserId, japanNow.Year);
                    Logger.Error(ex,
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
