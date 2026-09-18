using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
                "`/ai message:<内容>`\n"
                + "AIにメッセージを送信します。\n"
                + "・サーバー内では、サーバーごとの会話履歴を考慮して回答します。\n"
                + "・DMでは会話履歴を使用しない単発応答です。\n"
                + "・AIサービスが停止中または利用できない場合は実行できません。\n\n"
                + "`/aichannel`\n"
                + "実行したチャンネルを、そのサーバーのAI会話チャンネルに設定します。設定後、そのチャンネルへ通常のメッセージを投稿するとAIが返信します。\n"
                + "・サーバー内でのみ利用できます。\n"
                + "・AI会話チャンネルはサーバーごとに1つです。\n"
                + "・BotやWebhookによる投稿はAIへの送信対象外です。\n"
                + "・管理者のみ利用できます。\n"
                + "・未設定の状態で実行すると有効になり、設定済みの状態で再実行すると無効になります。"),
            ["recruitment"] = ("✋ 募集・締切", ButtonStyle.Success,
                "`/bo`\n"
                + "参加者募集を作成します。引数はすべて任意です。\n\n"
                + "```text\n"
                + "content     募集内容\n"
                + "at          募集人数。募集主は含みません。\n"
                + "            3なら募集主を含めて4人で終了します。\n"
                + "rank        ランクや参加条件\n"
                + "deadline    締切日時。yyyy-MM-dd HH:mm形式\n"
                + "            例：2026-08-13 01:30\n"
                + "description 募集の説明\n"
                + "```\n\n"
                + "参加、参加取消、募集終了のボタンが表示されます。指定人数への到達、または締切日時の経過で自動終了します。\n"
                + "・`at`を未指定または`0`にすると、人数による自動終了はありません。\n"
                + "・締切日時は日本時間として扱われます。\n"
                + "・募集作成後に`/deadline`で締切を設定できます。\n"
                + "・締切未設定の募集は、作成から7日を超えると自動的に整理されます。\n"
                + "・終了後は同じ内容で再募集できます。\n\n"
                + "`/deadline`\n"
                + "自分が作成した直近の募集に締切を設定します。\n"
                + "・当日から25日分の日付を選択できます。\n"
                + "・時刻は5分単位です。\n"
                + "・操作できるのはコマンド実行者本人です。"),
            ["birthday"] = ("🎂 誕生日", ButtonStyle.Secondary,
                "`/birthday month:<月> day:<日> [user:<対象>] [year:<年>]`\n"
                + "誕生日を登録または更新します。`user`を省略すると自分が対象です。\n"
                + "・月は1～12、日は1～31で指定します。\n"
                + "・実在しない日付は登録できません。\n"
                + "・`year`を指定すると、誕生日通知に年齢が表示されます。\n"
                + "・サーバー内でのみ利用できます。\n\n"
                + "`/birthdaychannel`\n"
                + "実行したチャンネルを誕生日通知先に設定します。誕生日当日の日本時間に通知します。\n"
                + "・管理者のみ利用できます。\n"
                + "・未設定の状態で実行すると有効になり、設定済みの状態で再実行すると無効になります。"),
            ["coin"] = ("🪙 コイン・VC", ButtonStyle.Primary,
                "`/status`\n"
                + "自分の所持コイン、累計VC滞在時間、換金済み時間、換金可能時間、サーバー内のコイン順位、破産回数を表示します。\n\n"
                + "`/exchange`\n"
                + "未換金のVC滞在時間をコインへ換金します。換金レートは**6分 = 25コイン**で、6分未満の端数は次回へ繰り越されます。確認画面で「換金」を押すと確定します。\n\n"
                + "`/pay user:<相手> coin:<枚数>`\n"
                + "指定ユーザーへコインを送金します。1枚以上を指定してください。自分自身やBotには送金できず、残高不足の場合は送金されません。サーバー内でのみ利用できます。\n\n"
                + "`/richrank`\n"
                + "サーバー内のコイン保有量ランキングを表示します。Botを除く上位10名が表示されます。\n\n"
                + "`/vcchannel` または `/vclog`\n"
                + "VCへの参加、退出、チャンネル移動のログ表示を切り替えます。初回実行時は実行したテキストチャンネルをログ投稿先に設定し、設定済みの状態で再実行すると無効になります。管理者のみ利用できます。\n\n"
                + "`/vcrank period:<day|week|month|all>`\n"
                + "VC滞在時間のランキングを表示します。`day`は過去24時間、`week`は過去7日間、`month`は過去1か月、`all`は全期間です。最大10位までと実行者自身の順位が表示されます。"),
            ["casino"] = ("🎰 カジノ", ButtonStyle.Danger,
                "`/blackjack bet:<額>`\n"
                + "コインを賭けてブラックジャックをプレイします。ベット額は1以上かつ所持コイン以内です。\n"
                + "・`HIT`：カードを1枚引く\n・`STAND`：その時点で勝負する\n・`SURRENDER`：降参する\n"
                + "・Aは、合計が21を超えない範囲で1または11として扱います。\n"
                + "配当：BLACKJACKはベット額の3倍、勝利は2倍、PUSHはそのまま返還、SURRENDERは50%返還、敗北・BUSTは0です。\n\n"
                 + "`/reversistart [bet:<額>]`\n"
                 + "2人対戦のリバーシを開始します。`bet`を省略すると通常対局、指定すると1人あたりのベット額になります。\n"
                 + "・`bet`は正の整数です。\n"
                 + "・2人目の参加時に両者から基本ベット額を徴収します。開始前に各プレイヤーが基本ベット額を所持している必要があります。\n"
                 + "・ゲーム開始時のfieldはベット額×2です。\n"
                 + "・終了時は実際の勝者・敗者の石数比から倍率を計算します。盤面に空きマスが残る40:23のような結果にも対応します。\n"
                 + "・基準値：33:31は1.0倍、36:28は1.2倍、40:24は1.8倍、44:20は2.4倍、48:16は3.4倍、52:12は4.7倍、56:8は6.2倍、58:6は7.0倍、60:4と62:2は8.0倍です。基準値の間は補間し、通常倍率は8.0倍が上限です。\n"
                 + "・64:0の完勝（相手の石が0個）は特別に10.0倍です。\n"
                 + "・勝者への払い戻しはfield×倍率です。不足分は敗者の残高を0まで追加徴収し、残高が不足していても勝者には全額を支払います。引き分けは両者へ基本ベット額を返金します。\n\n"
                + "`/doubleup bet:<額>`\n"
                + "カードの数字が7より上か下かを予想します。ベット額は1以上かつ所持コイン以内です。\n"
                + "・`LOW`：1～6　・`7`：SPECIAL。勝敗を発生させず継続　・`HIGH`：8～13\n"
                + "・`WIN`で賞金が2倍、`LOSE`で賞金0となりゲーム終了です。\n"
                + "・`CASH OUT`で賞金を確定できます。最大倍率は128倍で、サーバーイベント中は当選倍率が2.4倍になる場合があります。\n\n"
                + "`/roulette bet:<額>`\n"
                + "数字を予想してルーレットを回します。ベット後に1、3、5、10、20から選択します。\n"
                + "```text\n予想数字   出現数（25マス中）   的中時の配当\n1          12                   2倍\n3          6                    4倍\n5          4                    6倍\n10         2                    11倍\n20         1                    21倍\n```\n\n"
                + "`/slot bet:<額>`\n"
                + "スロットを回します。ベット額は1以上かつ所持コイン以内です。\n"
                + "```text\n絵柄   出現率   3つ揃い   2つ揃い（リーチ）\n🍒     24%      8倍       0倍\n🍋     22%      12倍      0倍\n🍇     15%      20倍      0.5倍\n🍉     11%      35倍      1倍\n🍈     9%       55倍      1.5倍\n🔔     9%       160倍     4倍\n💎     6%       500倍     6倍\n7️⃣     4%       1000倍    10倍\n```\n"
                + "3つ揃いは対応倍率、2つ揃いはリーチ倍率で払い戻します。リーチ倍率が0倍の場合や、すべて異なる場合はハズレです。小数点以下は切り捨てます。\n\n"
                + "`/slotstatus`\nBot全体で共有されるスロット統計（累計回転数、最長ハマり回数、最短当たり回数）を表示します。\n\n"
                + "`/mines bet:<額>`\n"
                + "5列×4行の20マスから安全マスを開けます。ベット額は1以上かつ所持コイン以内です。爆弾は4個で、安全マスを開けるほど倍率が上がります。`CASH OUT`で回収でき、爆弾を開けると払い戻しなしで終了します。16個すべての安全マスを開けると自動回収されます。\n"
                + "倍率：0～1マス ×1.0、2 ×1.5、3 ×2.0、4 ×2.5、5 ×3.5、6 ×5、7 ×7、8 ×10、9 ×14、10 ×22、11 ×35、12 ×60、13 ×110、14 ×250、15 ×600、16 ×5000。\n\n"
                + "`/lastchance`\n"
                + "所持コインが0のときに1回だけ利用できる復活ゲームです。開始すると破産回数が1回増えます。安定は1,000コインを確実に獲得、ギャンブルは0～3,000コイン、一発逆転は5,000コインまたは0コイン（5%で5,000コイン）です。サーバー内でのみ利用できます。"),
            ["event"] = ("🔥 サーバーイベント", ButtonStyle.Success,
                "`/serverevent`\n"
                + "選択画面からサーバーイベントを発動します。表示される必要コストは、発動時点の実装値に基づきます。\n\n"
                + "```text\n"
                + "💰 倍返しキャンペーン！\nコスト：TOP10合計コインの50%\n効果：30分間、ブラックジャック・DOUBLE UP・スロット・ルーレットの払い戻しが1.5倍\n\n"
                + "🎰 激アツスロット×10！\nコスト：TOP10合計コインの50%\n効果：10分間、🍒16% / 🍋14% / 🍇10% / 🍉8% / 🍈10% / 🔔19% / 💎14% / 7️⃣9%\n\n"
                + "🛡️ 50% BACK保証\nコスト：TOP10合計コインの30%\n効果：30分間、対象ゲームで負けた場合にベット額の50%を返還\n\n"
                + "🍀 豪運に幸あれ！\nコスト：TOP10合計コインの10%\n効果：20分間、ブラックジャック・スロット・ルーレットの払い戻しが1.25倍\n\n"
                + "🪙 ブラックジャック保険\nコスト：発動者の所持コインの50%\n効果：10分間、サレンダー時にベット額の80%を返還\n\n"
                + "🔥 倍倍倍プッシュ！！\nコスト：20,000コイン\n効果：10分間、DOUBLE UPの当選倍率を2.4倍に変更\n\n"
                + "💣 Mines安全週間1\nコスト：発動者の所持コインの20%\n効果：10分間、爆弾を1個減少（4個→3個）\n\n"
                + "💣 Mines安全週間2\nコスト：発動者の所持コインの40%\n効果：10分間、爆弾を2個減少（4個→2個）\n\n"
                + "💣 Mines安全週間3\nコスト：発動者の所持コインの95%\n効果：10分間、爆弾を3個減少（4個→1個）\n\n"
                + "💎 生きるか死ぬか\nコスト：発動者の所持コインの50%\n効果：次の1ゲーム限定。勝利時は予定払い戻しが5倍、敗北時はゲーム後の所持コインの50%を失う\n"
                + "```\n"
                + "通常のサーバーイベントは同時に1つだけ発動できます。「生きるか死ぬか」は個人イベントのため、サーバーイベントと同時に発動できます。発動コストの割合は、発動者またはサーバー内TOP10の所持コインを基準に計算します。"),
            ["admin"] = ("⚙️ 管理・設定", ButtonStyle.Secondary,
                "`/rolechannel`\n"
                + "実績解除通知の投稿先を、実行したチャンネルに設定します。\n"
                + "・管理者のみ利用できます。\n"
                + "・未設定の状態で実行すると、実績解除通知が有効になります。\n"
                + "・設定済みの状態で再実行すると、実績解除通知が無効になります。\n\n"
                + "AI、誕生日、VCログの各通知・会話チャンネル設定は、それぞれのカテゴリから確認できます。\n\n"
                + "`主な制限`\nサーバー専用コマンドはDMで実行できません。\nベットや送金には残高が必要です。\n入力値や日時形式が不正な場合はエラーになります。\nゲーム中は同じゲームを重複して開始できず、ゲームのボタンを操作できるのは開始者本人です。\n外部サービス障害時はAI機能を利用できません。")
        };

    [SlashCommand("help", "TacoDiscordBotの利用ガイドを表示します")]
    public async Task Help(InteractionContext ctx)
    {
        var options = Categories
            .Select(category => new DiscordSelectComponentOption(
                category.Value.Label,
                category.Key,
                $"{category.Value.Label} の利用方法を表示します。"))
            .ToList();
        var select = new DiscordSelectComponent(
            $"{ComponentPrefix}:select:{ctx.User.Id}",
            "カテゴリを選択してください",
            options,
            false,
            1,
            1);
        var response = new DiscordInteractionResponseBuilder()
            .WithContent("📖 **TacoDiscordBot 利用ガイド**\n\nカテゴリを選択してください。")
            .AddComponents(select);

        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            response.AsEphemeral(true));
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
            || parts[1] != "select"
            || !ulong.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ownerId))
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

        var categoryKey = e.Interaction.Data.Values?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(categoryKey) || !Categories.TryGetValue(categoryKey, out var category))
            return;

        await e.Interaction.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .AddEmbed(new DiscordEmbedBuilder()
                    .WithTitle(category.Label)
                    .WithDescription(category.Guide)
                    .Build())
                .AsEphemeral(true));
    }

}
