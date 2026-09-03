using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace TacoDiscordBot.Services.Interface;

public interface IAiService
{
    // 会話履歴を持たない AI 応答を取得します。
    Task<string> SendToGeminiAsync(string prompt);

    // ギルド単位の会話履歴を考慮した AI 応答を取得します。
    Task<string> SendToGeminiAsync(ulong guildId, string prompt);

    // Discord のメッセージ作成イベントを処理します。
    Task HandleMessageCreated(DiscordClient sender, MessageCreateEventArgs e);
}