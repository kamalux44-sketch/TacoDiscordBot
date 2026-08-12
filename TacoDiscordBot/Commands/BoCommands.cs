using System.Threading.Tasks;
using DSharpPlus.SlashCommands;

namespace TacoDiscordBot.Commands;

public class BoCommands : ApplicationCommandModule
{
    [SlashCommand("bo", "募集を作成します")]
    public async Task Bo(InteractionContext ctx,
        [Option("game","ゲーム名")] string game,
        [Option("at","参加人数(任意)")] long at = 0,
        [Option("rank","ランク(任意)")] string rank = "")
    {
        await BotHost.BoManager.CreateSessionAsync(ctx, game, (int)at, rank);
    }
}
