using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Commands;

public class DeadlineCommands : ApplicationCommandModule
{
    [SlashCommand("deadline", "締め切りを設定します")]
    public async Task Deadline(InteractionContext ctx)
    {
        Logger.Info(
            $"Deadline command invoked by User={ctx.User.Id} Guild={ctx.Guild?.Id}"
        );

        try
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

                dateOptions.Add(
                    new DiscordSelectComponentOption(
                        label,
                        value,
                        description
                    )
                );
            }

            // 時/分の選択肢は下でまとめて作成します

            // ==================================================
            // Select Menu
            //
            // DSharpPlus 5.0.0:
            //
            // DiscordSelectComponent(
            //     customId,
            //     placeholder,
            //     options,
            //     disabled,
            //     minOptions,
            //     maxOptions
            // )
            // ==================================================

            var dateSelect = new DiscordSelectComponent(
                $"deadline_date:{ctx.User.Id}",
                $"日付: {now:MM/dd}",
                dateOptions,
                false,
                1,
                1
            );

            // 現在時刻を5分単位に切り捨て
            var initialMinute = (now.Minute / 5) * 5;

            // ==================================================
            // Selects for hour and minute (00-23, 5-min steps)
            // ==================================================
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

            var hourSelect = new DiscordSelectComponent(
                $"deadline_hour:{ctx.User.Id}",
                $"時: {now:HH}",
                hourOptions,
                false,
                1,
                1
            );

            var minuteSelect = new DiscordSelectComponent(
                $"deadline_min:{ctx.User.Id}",
                $"分: {initialMinute:00}",
                minuteOptions,
                false,
                1,
                1
            );

            // ==================================================
            // Buttons
            // ==================================================

            var confirmButton = new DiscordButtonComponent(
                ButtonStyle.Success,
                $"deadline_confirm:{ctx.User.Id}",
                "決定"
            );

            var cancelButton = new DiscordButtonComponent(
                ButtonStyle.Danger,
                $"deadline_cancel:{ctx.User.Id}",
                "キャンセル"
            );

            // ==================================================
            // Response
            // ==================================================

            var response = new DiscordInteractionResponseBuilder()
                .WithContent(
                    "**締め切り日時を選択してください**\n" +
                    $"現在時刻: `{now:yyyy/MM/dd HH:mm}`"
                );

            // Row 1: 日付
            response.AddComponents(dateSelect);

            // Row 2: 時
            response.AddComponents(hourSelect);

            // Row 3: 分
            response.AddComponents(minuteSelect);

            // Row 4: 決定 / キャンセル
            response.AddComponents(confirmButton);
            response.AddComponents(cancelButton);

            Logger.Info(
                $"Deadline: sending response " +
                $"User={ctx.User.Id} " +
                $"dateOptions={dateOptions.Count} " +
                $"hourOptions={hourOptions.Count} " +
                $"minuteOptions={minuteOptions.Count}"
            );

            // ==================================================
            // Interaction Response
            // ==================================================

            // Create interaction response directly (non-ephemeral)
            Logger.Info($"Deadline: sending response for User={ctx.User.Id}");
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, response);
            Logger.Info($"Deadline: UI posted for User={ctx.User.Id}");
        }
        catch (Exception ex)
        {
            Logger.Error(
                ex,
                $"Deadline command failed User={ctx.User.Id}"
            );

            // 既に Deferred 応答済みの可能性が高いため、EditResponseAsync でエラーメッセージを上書きします
            try
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("❌ 締め切り設定UIの表示に失敗しました。"));
            }
            catch (Exception responseEx)
            {
                Logger.Error(responseEx, "Failed to send Deadline error response via EditResponseAsync");
                try
                {
                    // 最終手段: CreateResponse を試す
                    await ctx.CreateResponseAsync(
                        InteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder().WithContent("❌ 締め切り設定UIの表示に失敗しました。")
                    );
                }
                catch (Exception finalEx)
                {
                    Logger.Error(finalEx, "Failed to send Deadline error response via CreateResponseAsync");
                }
            }
        }
    }
}
