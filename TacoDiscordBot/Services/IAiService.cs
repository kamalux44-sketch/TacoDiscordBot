using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace TacoDiscordBot.Services;

public interface IAiService
{
    Task<string> SendToGeminiAsync(string prompt);

    Task HandleMessageCreated(DiscordClient sender, MessageCreateEventArgs e);
}