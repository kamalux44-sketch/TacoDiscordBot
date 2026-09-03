using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace TacoDiscordBot.Services.Interface;

public interface IVcRankingService
{
    // VC セッションの開始・終了をボイス状態更新から処理します。
    Task HandleVoiceStateUpdated(DiscordClient client, VoiceStateUpdateEventArgs e);

    // 指定期間の VC 滞在時間ランキングを作成します。
    Task<DiscordEmbedBuilder> BuildRankingEmbedAsync(
        ulong guildId,
        string period,
        DiscordGuild guild,
        DiscordUser requestingUser
    );
}