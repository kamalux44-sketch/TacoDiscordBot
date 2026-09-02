using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Contexts;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Commands;

public class DeadlineCommands : ApplicationCommandModule
{
    [SlashCommand("deadline", "締め切りを設定します")]
    public async Task Deadline(InteractionContext ctx)
    {
        Logger.Info($"Deadline command invoked by User={ctx.User.Id} Guild={ctx.Guild?.Id}");

        await DeadlineAsync(new InteractionResponseContext(ctx), ctx.User.Id);
    }

    public async Task DeadlineAsync(IInteractionResponseContext response, ulong userId)
    {

        var now = DateTime.Now;

        // ==================================================
        // 日付
        // 今日から25日分
        // ==================================================

        var dateOptions = new List<DiscordSelectComponentOption>();

        for (int i = 0; i < 25; i++)
        {
            var date = now.Date.AddDays(i);

            var label = date.ToString("MM/dd");
            var value = date.ToString("yyyy-MM-dd");
            var description = date.ToString("yyyy/MM/dd");

            dateOptions.Add(new DiscordSelectComponentOption(label, value, description));
        }

        // ==================================================
        // 時間
        // ==================================================

        var initialMinute = (now.Minute / 5) * 5;

        var hourOptions = new List<DiscordSelectComponentOption>();

        for (int hour = 0; hour < 24; hour++)
        {
            var value = hour.ToString("00");

            hourOptions.Add(new DiscordSelectComponentOption(value, value));
        }

        var minuteOptions = new List<DiscordSelectComponentOption>();

        for (int minute = 0; minute < 60; minute += 5)
        {
            var value = minute.ToString("00");

            minuteOptions.Add(new DiscordSelectComponentOption(value, value));
        }

        // ==================================================
        // Select Menu
        // ==================================================

        var dateSelect = new DiscordSelectComponent(
            $"deadline_date:{userId}",
            $"{now:MM/dd}",
            dateOptions,
            false,
            1,
            1
        );

        var hourSelect = new DiscordSelectComponent(
            $"deadline_hour:{userId}",
            $"{now:HH}",
            hourOptions,
            false,
            1,
            1
        );

        var minuteSelect = new DiscordSelectComponent(
            $"deadline_min:{userId}",
            $"{initialMinute:00}",
            minuteOptions,
            false,
            1,
            1
        );

        // ==================================================
        // Button
        // ==================================================

        var confirmButton = new DiscordButtonComponent(
            ButtonStyle.Success,
            $"deadline_confirm:{userId}",
            "決定"
        );

        // ==================================================
        // Response
        // ==================================================

        Logger.Info(
            $"Deadline: sending response "
                + $"User={userId} "
                + $"dateOptions={dateOptions.Count} "
                + $"hourOptions={hourOptions.Count} "
                + $"minuteOptions={minuteOptions.Count}"
        );

        await response.RespondWithComponentsAsync(
            "**締め切り日時を選択してください**\n",
            new DiscordComponent[] { dateSelect, hourSelect, minuteSelect, confirmButton }
        );

        Logger.Info($"Deadline: UI posted for User={userId}");
    }
}
