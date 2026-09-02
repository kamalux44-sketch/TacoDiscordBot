using System.Threading.Tasks;
using System.Collections.Generic;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;

namespace TacoDiscordBot.Contexts;

public sealed class InteractionResponseContext : IInteractionResponseContext
{
    private readonly InteractionContext _context;

    public InteractionResponseContext(InteractionContext context)
    {
        _context = context;
    }

    public async Task RespondAsync(string content, bool ephemeral = false)
    {
        await _context.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(content).AsEphemeral(ephemeral)
        );
    }

    public async Task DeferResponseAsync()
    {
        await _context.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
    }

    public async Task EditResponseAsync(string content)
    {
        await _context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(content));
    }

    public async Task EditResponseAsync(DiscordEmbed embed)
    {
        await _context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
    }

    public async Task RespondWithComponentsAsync(
        string content,
        IReadOnlyList<DiscordComponent> components
    )
    {
        var response = new DiscordInteractionResponseBuilder()
            .WithContent(content)
            .AsEphemeral(true);

        foreach (var component in components)
            response.AddComponents(component);

        await _context.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, response);
    }
}
