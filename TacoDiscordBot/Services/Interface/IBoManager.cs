using System;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;

namespace TacoDiscordBot.Services.Interface;

public interface IBoManager
{
    // 募集に関するコンポーネント操作を処理します。
    Task HandleComponentInteraction(
        DiscordClient client,
        ComponentInteractionCreateEventArgs e
    );

    // 新しい募集セッションを作成します。
    Task CreateSessionAsync(
        InteractionContext context,
        string body,
        int at,
        string rank,
        DateTime? deadline = null,
        string description = ""
    );
}