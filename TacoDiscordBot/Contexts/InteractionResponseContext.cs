using System.Threading.Tasks;
using System.Collections.Generic;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;

namespace TacoDiscordBot.Contexts;

public sealed class InteractionResponseContext : IInteractionResponseContext
{
    private readonly InteractionContext _context;

    // Discord のインタラクションコンテキストを保持します。
    public InteractionResponseContext(InteractionContext context)
    {
        _context = context;
    }

    public async Task RespondAsync(string content, bool ephemeral = false)
    {
        // 指定された公開範囲で初回応答を作成します。
        await _context.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(content).AsEphemeral(ephemeral)
        );
    }

    public async Task DeferResponseAsync()
    {
        // Discord に遅延応答を通知します。
        await _context.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
    }

    public async Task EditResponseAsync(string content)
    {
        // 既存の応答本文を更新します。
        await _context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(content));
    }

    public async Task EditResponseAsync(DiscordEmbed embed)
    {
        // 既存の応答を Embed で更新します。
        await _context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
    }

    public async Task RespondWithComponentsAsync(
        string content,
        IReadOnlyList<DiscordComponent> components
    )
    {
        // コンポーネントを応答へ追加して初回メッセージを作成します。
        var response = new DiscordInteractionResponseBuilder()
            .WithContent(content)
            .AsEphemeral(true);

        foreach (var component in components)
            response.AddComponents(component);

        await _context.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, response);
    }
}
