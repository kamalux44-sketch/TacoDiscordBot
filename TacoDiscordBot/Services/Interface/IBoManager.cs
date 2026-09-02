using System;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;

namespace TacoDiscordBot.Services.Interface;

public interface IBoManager
{
    Task HandleComponentInteraction(
        DiscordClient client,
        ComponentInteractionCreateEventArgs e
    );

    Task CreateSessionAsync(
        InteractionContext context,
        string body,
        int at,
        string rank,
        DateTime? deadline = null,
        string description = ""
    );
}