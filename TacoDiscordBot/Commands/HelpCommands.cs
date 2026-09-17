using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;

namespace TacoDiscordBot.Commands;

public sealed class HelpCommands : ApplicationCommandModule
{
    private const string ComponentPrefix = "help";

    private static readonly IReadOnlyDictionary<string, (string Label, ButtonStyle Style, string Guide)> Categories =
        new Dictionary<string, (string Label, ButtonStyle Style, string Guide)>
        {
            ["ai"] = ("🤖 AI", ButtonStyle.Primary,
                "🤖 **AI**\n\n"
                + "`/ai message:<内容>`\nAIにメッセージを送信します。サーバーではサーバーごとの会話履歴を考慮し、DMでは単発応答になります。\n\n"
                + "`/aichannel`\n実行したチャンネルをAI会話チャンネルに設定します。通常のメッセージにAIが返信します。管理者のみ利用でき、再実行で無効になります。"),
            ["recruitment"] = ("✋ 募集・締切", ButtonStyle.Success,
                "✋ **募集・締切**\n\n"
                + "`/bo`\n参加者募集を作成します。募集内容、人数、ランク、締切日時、説明を指定できます。参加・参加取消・募集終了のボタンが表示されます。\n\n"
                + "`/deadline`\n自分が作成した直近の募集に締切を設定します。日付は当日から25日分、時刻は5分単位で選択できます。"),
            ["birthday"] = ("🎂 誕生日", ButtonStyle.Secondary,
                "🎂 **誕生日**\n\n"
                + "`/birthday month:<月> day:<日> [user:<対象>] [year:<年>]`\n誕生日を登録または更新します。userを省略すると自分が対象です。サーバー内でのみ利用できます。\n\n"
                + "`/birthdaychannel`\n実行したチャンネルを誕生日通知先に設定します。管理者のみ利用でき、再実行で無効になります。"),
            ["coin"] = ("🪙 コイン・VC", ButtonStyle.Primary,
                "🪙 **コイン・VC**\n\n"
                + "`/status`：所持コイン、VC滞在時間、換金状況、順位、破産回数を表示します。\n"
                + "`/exchange`：未換金のVC滞在時間を、6分=25コインで換金します。\n"
                + "`/pay user:<相手> coin:<枚数>`：コインを送金します。\n"
                + "`/richrank`：コイン保有量ランキングを表示します。\n"
                + "`/vcrank period:<day|week|month|all>`：VC滞在時間ランキングを表示します。\n"
                + "`/vcchannel` または `/vclog`：VCログの有効・無効を切り替えます。管理者のみ利用できます。"),
            ["casino"] = ("🎰 カジノ", ButtonStyle.Danger,
                "🎰 **カジノ**\n\n"
                + "`/blackjack bet:<額>`：ブラックジャックをプレイします。HIT、STAND、SURRENDERを選択できます。\n"
                + "`/doubleup bet:<額>`：カードが7より上か下かを予想します。CASH OUTで賞金を確定できます。\n"
                + "`/roulette bet:<額>`：1、3、5、10、20から予想してルーレットを回します。\n"
                + "`/slot bet:<額>`：スロットを回します。\n"
                + "`/slotstatus`：スロット統計を表示します。\n"
                + "`/mines bet:<額>`：20マスから安全マスを開けます。CASH OUTで回収できます。\n"
                + "`/lastchance`：所持コインが0のときに1回だけ利用できる復活ゲームです。"),
            ["event"] = ("🔥 サーバーイベント", ButtonStyle.Success,
                "🔥 **サーバーイベント**\n\n"
                + "`/serverevent`\n選択画面からサーバーイベントを発動します。ゲームの払い戻し倍率変更、スロット出現率変更、負けコイン保証、Minesの爆弾減少などの効果があります。\n\n"
                + "通常のサーバーイベントは同時に1つだけ発動できます。発動コストはイベントと発動者またはサーバー内TOP10の所持コインを基準に計算されます。"),
            ["admin"] = ("⚙️ 管理・設定", ButtonStyle.Secondary,
                "⚙️ **管理・設定**\n\n"
                + "`/rolechannel`\nロール解除通知の投稿先を、実行したチャンネルに設定します。管理者のみ利用でき、再実行で無効になります。\n\n"
                + "AI、誕生日、VCログの各通知・会話チャンネル設定は、それぞれのカテゴリから確認できます。")
        };

    [SlashCommand("help", "TacoDiscordBotの利用ガイドを表示します")]
    public async Task Help(InteractionContext ctx)
    {
        var firstRow = new DiscordComponent[]
        {
            CreateButton("ai", Categories["ai"], ctx.User.Id),
            CreateButton("recruitment", Categories["recruitment"], ctx.User.Id),
            CreateButton("birthday", Categories["birthday"], ctx.User.Id),
            CreateButton("coin", Categories["coin"], ctx.User.Id)
        };
        var secondRow = new DiscordComponent[]
        {
            CreateButton("casino", Categories["casino"], ctx.User.Id),
            CreateButton("event", Categories["event"], ctx.User.Id),
            CreateButton("admin", Categories["admin"], ctx.User.Id)
        };

        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent("📖 **TacoDiscordBot 利用ガイド**\n\nカテゴリを選択してください。")
                .AddComponents(firstRow)
                .AddComponents(secondRow)
                .AsEphemeral(true));
    }

    public static async Task HandleComponentInteractionAsync(
        DiscordClient client,
        ComponentInteractionCreateEventArgs e)
    {
        var customId = e.Interaction.Data.CustomId;
        if (string.IsNullOrWhiteSpace(customId) || !customId.StartsWith($"{ComponentPrefix}:", StringComparison.Ordinal))
            return;

        var parts = customId.Split(':');
        if (parts.Length != 3
            || !ulong.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ownerId)
            || !Categories.TryGetValue(parts[1], out var category))
            return;

        if (e.Interaction.User.Id != ownerId)
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent("このガイドを操作できるのは `/help` の実行者だけです。")
                    .AsEphemeral(true));
            return;
        }

        await e.Interaction.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent(category.Guide)
                .AsEphemeral(true));
    }

    private static DiscordButtonComponent CreateButton(
        string key,
        (string Label, ButtonStyle Style, string Guide) category,
        ulong userId)
        => new(category.Style, $"{ComponentPrefix}:{key}:{userId}", category.Label);
}
