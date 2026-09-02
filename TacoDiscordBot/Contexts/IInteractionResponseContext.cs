using System.Threading.Tasks;
using System.Collections.Generic;
using DSharpPlus.Entities;

namespace TacoDiscordBot.Contexts;

public interface IInteractionResponseContext
{
    Task RespondAsync(string content, bool ephemeral = false);

    Task DeferResponseAsync();

    Task EditResponseAsync(string content);

    Task EditResponseAsync(DiscordEmbed embed);

    Task RespondWithComponentsAsync(string content, IReadOnlyList<DiscordComponent> components);
}
