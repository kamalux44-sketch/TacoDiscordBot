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
            .AsEphemeral(true);

        // 各 Select は別々のアクション行に入れる（Select は各行1つまで）
        builder.AddComponents(new DiscordActionRowComponent(new DiscordComponent[] { dateSelect }));
        builder.AddComponents(new DiscordActionRowComponent(new DiscordComponent[] { hourSelect }));
        builder.AddComponents(new DiscordActionRowComponent(new DiscordComponent[] { minuteSelect }));

        // ボタンを1行にまとめる
        var confirmBtn = new DiscordButtonComponent(ButtonStyle.Success, $"deadline_confirm:{ctx.User.Id}", "✅ 決定");
        var cancelBtn = new DiscordButtonComponent(ButtonStyle.Danger, $"deadline_cancel:{ctx.User.Id}", "❌ キャンセル");
        builder.AddComponents(new DiscordActionRowComponent(new DiscordComponent[] { confirmBtn, cancelBtn }));

        Logger.Info($"Deadline: sending response to User={ctx.User.Id} dateOptions={dateOptions.Count} hourOptions={hourOptions.Count} minuteOptions={minuteOptions.Count}");
        try
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, builder);
            Logger.Info($"Deadline: response sent to User={ctx.User.Id}");
        }
        catch (DSharpPlus.Exceptions.BadRequestException bre)
        {
            Logger.Error(bre, "Deadline: CreateResponse full builder failed, attempting reduced builders");

            // 1) Try date select + buttons
            try
            {
                var altBuilder1 = new DiscordInteractionResponseBuilder()
                    .WithContent("締め切り日時を選択してください")
                    .AsEphemeral(true);
                altBuilder1.AddComponents(new DiscordActionRowComponent(new DiscordComponent[] { dateSelect }));
                altBuilder1.AddComponents(new DiscordActionRowComponent(new DiscordComponent[] { new DiscordButtonComponent(ButtonStyle.Success, $"deadline_confirm:{ctx.User.Id}", "✅ 決定"), new DiscordButtonComponent(ButtonStyle.Danger, $"deadline_cancel:{ctx.User.Id}", "❌ キャンセル") }));

                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, altBuilder1);
                Logger.Info($"Deadline: altBuilder1 response sent to User={ctx.User.Id}");
                return;
            }
            catch (DSharpPlus.Exceptions.BadRequestException bre2)
            {
                Logger.Error(bre2, "Deadline: altBuilder1 failed");
            }

            // 2) Try buttons-only (no selects)
            try
            {
                var altBuilder2 = new DiscordInteractionResponseBuilder()
                    .WithContent("締め切りUIを生成できませんでした。テキストで締め切りを指定してください（例: /bo deadline 2026-08-28 18:30）。")
                    .AsEphemeral(true)
                    .AddComponents(new DiscordComponent[] { new DiscordActionRowComponent(new DiscordComponent[] { new DiscordButtonComponent(ButtonStyle.Success, $"deadline_confirm:{ctx.User.Id}", "✅ 決定"), new DiscordButtonComponent(ButtonStyle.Danger, $"deadline_cancel:{ctx.User.Id}", "❌ キャンセル") }) });

                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, altBuilder2);
                Logger.Info($"Deadline: altBuilder2 response sent to User={ctx.User.Id}");
                return;
            }
            catch (Exception bre3)
            {
                Logger.Error(bre3, "Deadline: altBuilder2 failed");
            }

            // 最終フォールバック
            try
            {
                await ctx.CreateResponseAsync(
                    InteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder()
                        .WithContent("締め切りUIの作成に失敗しました。代替のテキスト入力で締め切りを指定してください。例: /bo deadline 2026-08-28 18:30")
                        .AsEphemeral(true));
            }
            catch (Exception rex)
            {
                Logger.Error(rex, "Failed to send final fallback response for Deadline command");
            }
        }
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
