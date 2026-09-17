using System;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Services;

namespace TacoDiscordBot.Commands;

public sealed class RouletteCommands : ApplicationCommandModule
{
    private const string CustomIdPrefix = "roulette:select";
    private static readonly int[] SelectableNumbers = [1, 3, 5, 10, 20];

    [SlashCommand("roulette", "ルーレットを回します")]
    public async Task Roulette(
        InteractionContext ctx,
        [Option("bet", "1以上、所持Scrap以内のベット額")] long bet
    )
    {
        if (ctx.Guild == null)
        {
            await RespondErrorAsync(ctx, "このコマンドはサーバー内で実行してください。");
            return;
        }

        var service = BotHost.RouletteService;
        if (service == null)
        {
            await RespondErrorAsync(ctx, "ルーレットサービスは未設定です。");
            return;
        }

        try
        {
            // 数字選択前にベットを先払いで徴収します。
            await service.StartAsync(ctx.Guild.Id, ctx.User.Id, bet);
            var buttons = new DiscordComponent[SelectableNumbers.Length];
            for (var index = 0; index < SelectableNumbers.Length; index++)
            {
                var number = SelectableNumbers[index];
                buttons[index] = new DiscordButtonComponent(
                    ButtonStyle.Primary,
                    $"{CustomIdPrefix}:{ctx.Guild.Id}:{ctx.User.Id}:{number}",
                    number.ToString()
                );
            }

            await ctx.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .AddEmbed(CreateSelectionEmbed(bet, service))
                    .AddComponents(new DiscordComponent[]
                    {
                        buttons[0], buttons[1], buttons[2], buttons[3], buttons[4]
                    })
            );
        }
        catch (ArgumentOutOfRangeException ex)
        {
            await RespondErrorAsync(ctx, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            await RespondErrorAsync(ctx, ex.Message);
        }
    }

    public static async Task HandleComponentInteractionAsync(
        DiscordClient client,
        ComponentInteractionCreateEventArgs e
    )
    {
        var customId = e.Interaction.Data.CustomId;
        if (string.IsNullOrWhiteSpace(customId) || !customId.StartsWith(CustomIdPrefix, StringComparison.Ordinal))
            return;

        var parts = customId.Split(':');
        if (parts.Length != 5
            || !ulong.TryParse(parts[2], out var guildId)
            || !ulong.TryParse(parts[3], out var ownerId)
            || !int.TryParse(parts[4], out var prediction))
            return;

        if (e.Interaction.User.Id != ownerId)
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent("⚠️ このルーレットを操作できるのは開始者だけです。")
                    .AsEphemeral(true)
            );
            return;
        }

        var service = BotHost.RouletteService;
        if (service == null)
            return;

        try
        {
            var hasResponded = false;
            var result = await service.SelectAsync(guildId, ownerId, prediction, async frame =>
            {
                if (!hasResponded)
                {
                    await e.Interaction.CreateResponseAsync(
                        InteractionResponseType.UpdateMessage,
                        new DiscordInteractionResponseBuilder().AddEmbed(frame)
                    );
                    hasResponded = true;
                    return;
                }

                await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbed(frame));
            });

            if (hasResponded)
                await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbed(result.Embed));
            else
                await e.Interaction.CreateResponseAsync(
                    InteractionResponseType.UpdateMessage,
                    new DiscordInteractionResponseBuilder().AddEmbed(result.Embed)
                );
        }
        catch (ArgumentOutOfRangeException ex)
        {
            await RespondComponentErrorAsync(e, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            await RespondComponentErrorAsync(e, ex.Message);
        }
    }

    private static DiscordEmbed CreateSelectionEmbed(long bet, RouletteService service)
        => new DiscordEmbedBuilder()
            .WithTitle("🎲 ルーレット")
            .WithDescription(
                $"賭け金: {bet:N0} Scrap\n\n"
                + "数字ボタンを選択してください。\n\n"
                + string.Join(
                    "\n",
                    SelectableNumbers.Select(number =>
                        $"{number}: {service.Configuration.Wheel.Count(value => value == number)}/25　倍率 ×{service.Configuration.PayoutMultipliers[number]}"))
            )
            .WithColor(DiscordColor.Blurple)
            .Build();

    private static Task RespondErrorAsync(InteractionContext ctx, string message)
        => ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(message).AsEphemeral(true)
        );

    private static Task RespondComponentErrorAsync(ComponentInteractionCreateEventArgs e, string message)
        => e.Interaction.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(message).AsEphemeral(true)
        );
}
