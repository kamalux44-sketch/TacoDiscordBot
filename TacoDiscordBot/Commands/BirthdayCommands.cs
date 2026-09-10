using System;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Contexts;
using TacoDiscordBot.Services;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Commands;

public sealed class BirthdayCommands : ApplicationCommandModule
{
    [SlashCommand("birthday", "誕生日を登録または更新します")]
    public async Task Birthday(
        InteractionContext ctx,
        [Option("month", "誕生月（1〜12）")] long month,
        [Option("day", "誕生日（1〜31）")] long day,
        [Option("user", "対象ユーザー（未指定時は自分）")] DiscordUser user = null,
        [Option("year", "誕生年（任意）")] long? year = null
    )
    {
        Logger.Info(
            "Birthday command invoked guild={GuildId} user={InvokerId} target={TargetUserId} year={Year} month={Month} day={Day}",
            ctx.Guild?.Id,
            ctx.User.Id,
            user?.Id ?? ctx.User.Id,
            year,
            month,
            day
        );

        if (ctx.Guild == null)
        {
            await new InteractionResponseContext(ctx).RespondAsync(Strings.CommandGuildOnly, true);
            return;
        }

        var service = BotHost.BirthdayService;
        if (service == null)
        {
            await new InteractionResponseContext(ctx).RespondAsync("誕生日サービスは未設定です。", true);
            return;
        }

        var targetUserId = user?.Id ?? ctx.User.Id;
        int? targetYear = year.HasValue ? checked((int)year.Value) : null;
        var error = await service.RegisterAsync(targetUserId, targetYear, checked((int)month), checked((int)day));

        if (error != null)
            Logger.Info("Birthday registration rejected user={UserId} reason={Reason}", targetUserId, error);
        else
            Logger.Info("Birthday registration completed user={UserId}", targetUserId);

        await new InteractionResponseContext(ctx).RespondAsync(
            error ?? $"<@{targetUserId}> の誕生日を登録しました。",
            true
        );
    }

    [SlashCommand("birthdaychannel", "誕生日メッセージの投稿先をこのチャンネルに設定します")]
    public async Task BirthdayChannel(InteractionContext ctx)
    {
        Logger.Info(
            "Birthday channel command invoked guild={GuildId} channel={ChannelId} user={UserId}",
            ctx.Guild?.Id,
            ctx.Channel.Id,
            ctx.User.Id
        );

        if (ctx.Guild == null)
        {
            await new InteractionResponseContext(ctx).RespondAsync(Strings.CommandGuildOnly, true);
            return;
        }

        var service = BotHost.BirthdayService;
        if (service == null)
        {
            await new InteractionResponseContext(ctx).RespondAsync("誕生日サービスは未設定です。", true);
            return;
        }

        await service.SetChannelAsync(ctx.Guild.Id, ctx.Channel.Id);
        Logger.Info("Birthday channel setting completed guild={GuildId} channel={ChannelId}", ctx.Guild.Id, ctx.Channel.Id);
        await new InteractionResponseContext(ctx).RespondAsync(
            "誕生日メッセージの投稿先をこのチャンネルに設定しました。",
            true
        );
    }
}
