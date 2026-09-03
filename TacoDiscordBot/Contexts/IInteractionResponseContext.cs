using System.Threading.Tasks;
using System.Collections.Generic;
using DSharpPlus.Entities;

namespace TacoDiscordBot.Contexts;

public interface IInteractionResponseContext
{
    // 通常のメッセージ応答を作成します。
    Task RespondAsync(string content, bool ephemeral = false);

    // 応答を遅延させ、後続処理の時間を確保します。
    Task DeferResponseAsync();

    // 既存の応答本文を更新します。
    Task EditResponseAsync(string content);

    // 既存の応答を Embed で更新します。
    Task EditResponseAsync(DiscordEmbed embed);

    // コンポーネント付きのメッセージ応答を作成します。
    Task RespondWithComponentsAsync(string content, IReadOnlyList<DiscordComponent> components);
}
