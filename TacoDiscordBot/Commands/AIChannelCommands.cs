using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Commands;

public class AIChannelCommands : ApplicationCommandModule
{
    [SlashCommand("aichannel", "このチャンネルを AI 会話チャンネルとして設定します（管理用）")]
    public async Task AiChannel(InteractionContext ctx)
    {
        // ギルドと AI チャンネルサービスの設定を確認してから、対象チャンネルを登録します。
        var guildId = ctx.Guild?.Id ?? 0UL;

        if (guildId == 0)
        {
            await ctx.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(Strings.CommandGuildOnly)
                    .AsEphemeral(true)
            );

            return;
        }

        if (BotHost.AiChannelService == null)
        {
            await ctx.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent(
                    "AI サービスは未設定です。管理者が DB と GEMINI_API_KEY を設定しているか確認してください。"
                )
            );

            return;
        }

        await BotHost.AiChannelService.SetChannelAsync(guildId, ctx.Channel.Id);

        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent(
                    $"このチャンネルを AI 会話チャンネルとして設定しました。 (#{ctx.Channel.Name})"
                )
                .AsEphemeral(true)
        );
    }
}
