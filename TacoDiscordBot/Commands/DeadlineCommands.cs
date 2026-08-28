using System;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using System.Collections.Generic;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Commands;

public class DeadlineCommands : ApplicationCommandModule
{
    [SlashCommand("deadline", "締め切りを設定します（本人のみ表示のUI）")]
    public async Task Deadline(InteractionContext ctx)
    {
        Logger.Info($"Deadline command invoked by User={ctx.User.Id} Guild={ctx.Guild?.Id}");
        // 応答はエフェメラル（本人のみ）でコンポーネントを表示します
        try
        {
        var now = DateTime.Now;

        // 日は当日から +24日まで（計25日）
        var daysToShow = 25;

        var dateOptions = new List<DiscordSelectComponentOption>();
        for (int i = 0; i < daysToShow; i++)
        {
            var d = now.Date.AddDays(i);
            var label = d.ToString("MM/dd");
            var value = d.ToString("yyyy-MM-dd");
            dateOptions.Add(new DiscordSelectComponentOption(label, value, d.ToString("yyyy/MM/dd")));
        }

        var hourOptions = new List<DiscordSelectComponentOption>();
        for (int h = 0; h < 24; h++)
        {
            var txt = h.ToString("00");
            hourOptions.Add(new DiscordSelectComponentOption(txt, txt, null));
        }

        var minuteOptions = new List<DiscordSelectComponentOption>();
        var initMinute = (now.Minute / 5) * 5;
        for (int m = 0; m < 60; m += 5)
        {
            var txt = m.ToString("00");
            minuteOptions.Add(new DiscordSelectComponentOption(txt, txt, null));
        }

        var datePlaceholder = now.ToString("MM/dd");
        var dateSelect = new DiscordSelectComponent($"deadline_date:{ctx.User.Id}", datePlaceholder, dateOptions, false, 1, 1);
        var hourPlaceholder = now.Hour.ToString("00");
        var hourSelect = new DiscordSelectComponent($"deadline_hour:{ctx.User.Id}", hourPlaceholder, hourOptions, false, 1, 1);
        var minutePlaceholder = initMinute.ToString("00");
        var minuteSelect = new DiscordSelectComponent($"deadline_min:{ctx.User.Id}", minutePlaceholder, minuteOptions, false, 1, 1);

        var builder = new DiscordInteractionResponseBuilder()
            .WithContent("締め切り日時を選択してください")
            .AsEphemeral(true)
            .AddComponents(new DiscordComponent[]
            {
                dateSelect,
                hourSelect,
                minuteSelect,
                new DiscordButtonComponent(ButtonStyle.Success, $"deadline_confirm:{ctx.User.Id}", "✅ 決定"),
                new DiscordButtonComponent(ButtonStyle.Danger, $"deadline_cancel:{ctx.User.Id}", "❌ キャンセル")
            });
        Logger.Info($"Deadline: sending response to User={ctx.User.Id}");
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, builder);
        Logger.Info($"Deadline: response sent to User={ctx.User.Id}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Deadline command failed");
            try
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder().WithContent("内部エラーが発生しました。管理者に連絡してください。").AsEphemeral(true));
            }
            catch (Exception rex)
            {
                Logger.Error(rex, "Failed to send error response for Deadline command");
            }
        }
    }
}
