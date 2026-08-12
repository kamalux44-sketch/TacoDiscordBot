using System.Threading.Tasks;
using DSharpPlus.SlashCommands;

namespace TacoDiscordBot.Commands;

public class BoCommands : ApplicationCommandModule
{
    [SlashCommand("bo", "募集を作成します")]
    public async Task Bo(InteractionContext ctx,
        [Option("game","ゲーム名")] string game,
        [Option("at","募集人数（募集主含めない。例: @3 は募集主＋3人）(任意)")] long at = 0,
        [Option("rank","ランク(任意)")] string rank = "",
        [Option("deadline","締め切り（任意、日時を選択してください）")] DateTime? deadline = null,
        [Option("description","募集の説明（任意）")] string description = "")
    {
        await BotHost.BoManager.CreateSessionAsync(ctx, game, (int)at, rank, deadline, description);
    }
}
