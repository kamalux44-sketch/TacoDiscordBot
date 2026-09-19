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
                + "AIにメッセージを送信します。\n\n"
                + "・サーバー内では、サーバーごとの会話履歴を考慮して回答します。\n"
                + "・DMでは会話履歴を使用しない単発応答です。\n"
                + "・AIサービスが停止中または利用できない場合は実行できません。\n\n"
                + "`/aichannel`\n"
                + "実行したチャンネルを、そのサーバーのAI会話チャンネルに設定します。\n"
                + "設定後、そのチャンネルへ通常のメッセージを投稿するとAIが返信します。\n\n"
                + "・サーバー内でのみ利用できます。\n"
                + "・AI会話チャンネルはサーバーごとに1つです。\n"
                + "・BotやWebhookによる投稿はAIへの送信対象外です。\n"
                + "・管理者のみ利用できます。\n"
                + "・再実行すると有効・無効が切り替わります。"),
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
                + "参加、参加取消、募集終了のボタンが表示されます。\n"
                + "指定人数への到達、または締切日時の経過で自動終了します。\n\n"
                + "・`at`を未指定または`0`にすると、人数による自動終了はありません。\n"
                + "・締切日時は日本時間として扱われます。\n"
                + "・募集作成後に`/deadline`で締切を設定できます。\n"
                + "・締切未設定の募集は、作成から7日を超えると自動的に整理されます。\n"
                + "・終了後は同じ内容で再募集できます。\n\n"
                + "`/deadline`\n"
                + "自分が作成した直近の募集に締切を設定します。\n\n"
                + "・当日から25日分の日付を選択できます。\n"
                + "・時刻は5分単位です。\n"
                + "・操作できるのはコマンド実行者本人です。"),
            ["birthday"] = ("🎂 誕生日", ButtonStyle.Secondary,
                "`/birthday month:<月> day:<日> [user:<対象>] [year:<年>]`\n"
                + "誕生日を登録または更新します。\n"
                + "`user`を省略すると自分が対象です。\n\n"
                + "・月は1～12、日は1～31で指定します。\n"
                + "・実在しない日付は登録できません。\n"
                + "・`year`を指定すると、誕生日通知に年齢が表示されます。\n"
                + "・サーバー内でのみ利用できます。\n\n"
                + "`/birthdaychannel`\n"
                + "実行したチャンネルを誕生日通知先に設定します。\n"
                + "誕生日当日の日本時間に通知します。\n\n"
                + "・管理者のみ利用できます。\n"
                + "・未設定の状態で実行すると有効になり、設定済みの状態で再実行すると無効になります。"),
            ["coin"] = ("🪙 コイン・VC", ButtonStyle.Primary,
                "`/status`\n"
                + "自分の所持コイン、累計VC滞在時間、換金済み時間、換金可能時間、\n"
                + "サーバー内のコイン順位、破産回数を表示します。\n\n"
                + "`/exchange`\n"
                + "未換金のVC滞在時間をコインへ換金します。\n"
                + "換金レートは**6分 = 25コイン**で、6分未満の端数は次回へ繰り越されます。\n"
                + "確認画面で「換金」を押すと確定します。\n\n"
                + "`/pay user:<相手> coin:<枚数>`\n"
                + "指定ユーザーへコインを送金します。\n"
                + "・1枚以上を指定してください。\n"
                + "・自分自身やBotには送金できません。\n"
                + "・残高不足の場合は送金されません。\n"
                + "・サーバー内でのみ利用できます。\n\n"
                + "`/richrank`\n"
                + "サーバー内のコイン保有量ランキングを表示します。Botを除く上位10名が表示されます。\n\n"
                + "`/vcchannel` または `/vclog`\n"
                + "VCへの参加、退出、チャンネル移動のログ表示を切り替えます。\n"
                + "初回実行時は実行したテキストチャンネルをログ投稿先に設定します。\n"
                + "設定済みの状態で再実行すると無効になります。\n"
                + "・管理者のみ利用できます。\n\n"
                + "`/vcrank period:<day|week|month|all>`\n"
                + "VC滞在時間のランキングを表示します。\n"
                + "・`day`：過去24時間\n"
                + "・`week`：過去7日間\n"
                + "・`month`：過去1か月\n"
                + "・`all`：全期間\n\n"
                 + "最大10位までと、実行者自身の順位が表示されます。"),
             ["shop"] = ("🛒 ロールショップ", ButtonStyle.Success,
                 "`/shop`\n"
                 + "コインでDiscordロールを購入します。\n\n"
                 + "カテゴリを選択し、購入するロールを選ぶと購入確認画面が表示されます。\n"
                 + "購入時に所持コインを再確認し、価格分を減算してロールを付与します。\n\n"
                 + "・購入済みのロールは再購入できません。\n"
                 + "・キャンセルするとロール一覧へ戻ります。\n"
                 + "・コインが不足している場合は購入されません。\n"
                 + "・購入履歴とコイン残高はBot再起動後も維持されます。\n"
                 + "・対象ロールがサーバーにない場合は、購入時に指定色で自動作成されます。\n"
                 + "・Botに対象ロールを付与する権限と、対象ロールより上位のロールが必要です。\n\n"
                 + "【お手頃ロール】\n"
                 + "🐣 ひよっこ：500 / 💤 寝不足：1,000 / 🧍 一般人：1,000\n"
                 + "🍵 ひとやすみ：1,500 / 🫠 ちょっと疲れた：2,000 / 👌 まあまあ：2,000\n"
                 + "🧐 様子見：2,500 / 🎲 運試し：3,000 / 💸 無駄遣い：4,000\n"
                 + "😎 余裕の顔：5,000 / 🥔 じゃがいも：5,000 / 👀 見てるだけ：5,000\n"
                 + "🧃 休憩中：5,000 / 🐢 のんびり勢：5,000 / 🤡 養分候補：5,000\n"
                 + "🤑 小銭持ち：5,000 / 💸 散財入門：5,000 / 🎲 勝負好き：5,000コイン\n\n"
                 + "【ステータスロール】\n"
                 + "💰 富裕層：1,000,000 / 💎 資産家：3,000,000 / 🏦 大口顧客：10,000,000\n"
                 + "💳 VIP：30,000,000 / ⭐ 特別会員：100,000,000 / 💠 プレミアム会員：300,000,000\n"
                 + "👑 ロイヤル会員：1,000,000,000 / 🏛️ 特別顧客：3,000,000,000\n"
                 + "💎 最上級顧客：10,000,000,000 / 👑 大富豪：30,000,000,000\n"
                 + "🏦 財閥：100,000,000,000 / 💰 大資産家：200,000,000,000\n"
                 + "💎 超富裕層：350,000,000,000 / 👑 資産家の頂点：500,000,000,000\n"
                 + "🏛️ 超大口顧客：650,000,000,000 / 💠 プレミアムVIP：750,000,000,000\n"
                 + "👑 ロイヤルVIP：850,000,000,000 / 🏆 特別待遇：1,000,000,000,000コイン\n\n"
                 + "【高額ロール】\n"
                 + "💰 札束で解決：10,000,000 / 🏦 金で殴るタイプ：50,000,000\n"
                 + "💎 金銭的余裕：100,000,000 / 👑 金ならある：500,000,000\n"
                 + "💳 支払いに躊躇なし：1,000,000,000 / 🤑 景気のいい人：5,000,000,000\n"
                 + "🏛️ スポンサー：10,000,000,000 / 💰 大口スポンサー：50,000,000,000\n"
                 + "💎 VIPスポンサー：100,000,000,000 / 👑 筆頭スポンサー：1,000,000,000,000コイン"),
            ["casino"] = ("🎰 カジノ", ButtonStyle.Danger,
                "`/blackjack bet:<額>`\n"
                + "コインを賭けてブラックジャックをプレイします。\n"
                + "ベット額は1以上かつ所持コイン以内です。\n\n"
                 + "```text\n操作       内容\nHIT        カードを1枚引く\nSTAND      その時点で勝負する\nSURRENDER  降参する\n```\n"
                 + "Aは、合計が21を超えない範囲で1または11として扱います。\n\n"
                 + "```text\n結果        配当\nBLACKJACK  ベット額の3倍\n勝利        ベット額の2倍\nPUSH        ベット額をそのまま返還\nSURRENDER   ベット額の50%を返還\n敗北・BUST  0\n```\n\n"
                 + "`/pokerstart coin:<参加費>`\n"
                 + "5 Card Draw Pokerの卓を作成します。`coin`は参加者1人あたりの参加費です。\n\n"
                 + "```text\n"
                 + "/pokerstart coin:500\n"
                 + "参加費       500 Coin\n"
                 + "初期Chip     1,000 Chip\n"
                 + "交換レート   1,000 Chip = 500 Coin\n"
                 + "```\n"
                 + "卓を作成すると4文字の卓番号と「参加」ボタンが表示されます。2人以上参加後に[開始]ボタンを押すとゲームが開始されます。\r\n最大6人まで参加できます。\n\n"
                 + "【CoinとChip】\n"
                 + "参加時に参加費をCoinから引き落とし、1,000 Poker Chipを付与します。ゲーム中はPoker ChipでBetし、終了時に残りChipを卓のレートでCoinへ払い戻します。\n"
                 + "払い戻しは`最終Chip × 参加費 ÷ 1,000`で計算し、Coinの小数点以下は切り捨てます。\n\n"
                 + "【ゲームの流れ】\n"
                 + "1. 参加ボタンで卓に参加\n"
                 + "2. 5枚の手札が配られ、Bet Round 1\n"
                 + "3. 0～5枚のカードを1回だけ交換\n"
                 + "4. Bet Round 2\n"
                 + "5. ショーダウン、Pot分配、Coin払い戻し\n\n"
                 + "【Bet操作】\n"
                 + "`Check`：誰もBetしていないときにChipを追加せず進む\n"
                 + "`Bet`：最低100 Chip、10 Chip単位で最初のBetをする\n"
                 + "`Call`：現在のBet額まで合わせる\n"
                 + "`Raise`：現在のBet額を最低100 Chip以上引き上げる\n"
                 + "`Fold`：ゲームから降りる。投入済みChipは戻らない\n"
                 + "`All-in`：残りChipをすべてBetする。100 Chip未満でも可能\n"
                 + "Bet後はCheckできません。操作できるのは現在の手番のプレイヤーだけです。Side Potは使用しません。\n\n"
                 + "【役の強さ】\n"
                 + "ロイヤルフラッシュ ＞ ストレートフラッシュ ＞ フォーカード ＞ フルハウス ＞ フラッシュ ＞ ストレート ＞ スリーカード ＞ ツーペア ＞ ワンペア ＞ ハイカード\n"
                 + "A・2・3・4・5もストレートとして認められます。同じ役はキッカーなどで比較し、完全に同じ場合はPotを均等分配します。\n\n"
                 + "【手札の表示】\n"
                  + "ゲーム中の手札は参加者本人だけに表示されます。他のプレイヤーの手札や交換内容は公開されません。手札メッセージを削除した場合は、公開卓の「🔄 手札を再表示」ボタンを押してください。\n\n"
                  + "ゲーム終了時のショーダウンでは、公開卓と各参加者のDMに全プレイヤーの手札、役、勝者、最終Chip数が表示されます。\n\n"
                  + "Pokerのゲーム状態は保存されるため、Bot再起動後も卓、手札、Chip、Pot、Bet状況を復元できます。公開卓メッセージが削除されていた場合は、安全のため卓を終了し、Potを参加者へ返却します。\n"
                 + "・参加費以上のCoinが必要です。\n"
                 + "・ゲーム開始後は参加できません。\n"
                 + "・Fold済み、交換済み、手番外の操作はできません。\n\n"
                 + "`/reversistart [bet:<額>]`\n"
                 + "2人対戦のリバーシを開始します。\n"
                 + "`bet`を省略すると通常対局、指定するとベット対局です。\n\n"
                  + "【開始条件】\n"
                  + "```text\n項目        内容\nbet         正の整数\n徴収        2人目の参加時に両者から基本ベット額を徴収\n必要残高    両者が基本ベット額を所持\nfield       ベット額×2\n```\n\n"
                 + "【倍率】\n"
                 + "終了時の実際の石数比から倍率を計算します。盤面に空きマスが残る40:23のような結果にも対応します。\n"
                 + "```text\n勝者：敗者  倍率\n33：31       1.0倍\n36：28       1.2倍\n40：24       1.8倍\n44：20       2.4倍\n48：16       3.4倍\n52：12       4.7倍\n56：8        6.2倍\n58：6        7.0倍\n60：4        8.0倍\n62：2        8.0倍\n```\n"
                 + "基準値の間は補間します。通常倍率の上限は8.0倍です。\n"
                 + "64:0の完勝だけは特別に10.0倍です。\n\n"
                  + "【精算】\n"
                  + "```text\n項目          内容\n払い戻し      field×倍率\n追加徴収      敗者の残高を0まで徴収\n残高不足      勝者には払い戻し額全額を支払い\n引き分け      両者へ基本ベット額を返金\n```\n\n"
                + "`/doubleup bet:<額>`\n"
                 + "カードの数字が7より上か下かを予想します。\n"
                 + "ベット額は1以上かつ所持コイン以内です。\n\n"
                 + "```text\n操作       内容\nLOW        1～6を予想\n7          SPECIAL。勝敗を発生させず継続\nHIGH       8～13を予想\nWIN        賞金が2倍\nLOSE       賞金0でゲーム終了\nCASH OUT   現在の賞金を確定\n```\n\n"
                 + "最大倍率は128倍です。\n"
                 + "サーバーイベント中は当選倍率が2.4倍になる場合があります。\n\n"
                + "`/roulette bet:<額>`\n"
                 + "数字を予想してルーレットを回します。\n"
                 + "ベット後に1、3、5、10、20から選択します。\n\n"
                + "```text\n予想数字   出現数（25マス中）   的中時の配当\n1          12                   2倍\n3          6                    4倍\n5          4                    6倍\n10         2                    11倍\n20         1                    21倍\n```\n\n"
                + "`/slot bet:<額>`\n"
                 + "スロットを回します。\n"
                 + "ベット額は1以上かつ所持コイン以内です。\n\n"
                + "```text\n絵柄   出現率   3つ揃い   2つ揃い（リーチ）\n🍒     24%      8倍       0倍\n🍋     22%      12倍      0倍\n🍇     15%      20倍      0.5倍\n🍉     11%      35倍      1倍\n🍈     9%       55倍      1.5倍\n🔔     9%       160倍     4倍\n💎     6%       500倍     6倍\n7️⃣     4%       1000倍    10倍\n```\n"
                 + "```text\n成立条件             扱い\n3つ揃い               対応倍率で払い戻し\n2つ揃い               リーチ倍率で払い戻し\nリーチ倍率が0倍      ハズレ\nすべて異なる          ハズレ\n```\n"
                 + "小数点以下は切り捨てます。\n\n"
                 + "`/slotstatus`\n"
                 + "Bot全体で共有されるスロット統計を表示します。\n"
                 + "・累計回転数\n"
                 + "・最長ハマり回数\n"
                 + "・最短当たり回数\n\n"
                + "`/mines bet:<額>`\n"
                 + "5列×4行の20マスから安全マスを開けます。\n"
                 + "ベット額は1以上かつ所持コイン以内です。\n"
                 + "爆弾は4個で、安全マスを開けるほど倍率が上がります。\n\n"
                 + "・`CASH OUT`で回収できます。\n"
                 + "・爆弾を開けると払い戻しなしで終了します。\n"
                 + "・16個すべての安全マスを開けると自動回収されます。\n\n"
                  + "```text\n安全マス数  倍率\n0～1         ×1.0\n2             ×1.5\n3             ×2.0\n4             ×2.5\n5             ×3.5\n6             ×5\n7             ×7\n8             ×10\n9             ×14\n10            ×22\n11            ×35\n12            ×60\n13            ×110\n14            ×250\n15            ×600\n16            ×5000\n```\n\n"
                + "`/lastchance`\n"
                 + "所持コインが0のときに1回だけ利用できる復活ゲームです。\n"
                 + "開始すると破産回数が1回増えます。\n\n"
                  + "```text\nモード      結果\n安定        1,000コインを確実に獲得\nギャンブル  0～3,000コイン\n一発逆転    5,000コインまたは0コイン\n            （5%で5,000コイン）\n```\n\n"
                 + "サーバー内でのみ利用できます。"),
            ["event"] = ("🔥 サーバーイベント", ButtonStyle.Success,
                "`/serverevent`\n"
                + "選択画面からサーバーイベントを発動します。\n"
                + "表示される必要コストは、発動時点の実装値に基づきます。\n\n"
                + "```text\n"
                 + "💰 倍返しキャンペーン！\n"
                 + "コスト：TOP10合計コインの50%\n"
                 + "効果：30分間、ブラックジャック・DOUBLE UP・\n"
                 + "      スロット・ルーレットの払い戻しが1.5倍\n\n"
                 + "🎰 激アツスロット×10！\n"
                 + "コスト：TOP10合計コインの50%\n"
                 + "効果：10分間、🍒16% / 🍋14% / 🍇10% / 🍉8% /\n"
                 + "      🍈10% / 🔔19% / 💎14% / 7️⃣9%\n\n"
                 + "🛡️ 50% BACK保証\n"
                 + "コスト：TOP10合計コインの30%\n"
                 + "効果：30分間、対象ゲームで負けた場合に\n"
                 + "      ベット額の50%を返還\n\n"
                 + "🍀 豪運に幸あれ！\n"
                 + "コスト：TOP10合計コインの10%\n"
                 + "効果：20分間、ブラックジャック・スロット・\n"
                 + "      ルーレットの払い戻しが1.25倍\n\n"
                 + "🪙 ブラックジャック保険\n"
                 + "コスト：発動者の所持コインの50%\n"
                 + "効果：10分間、サレンダー時にベット額の80%を返還\n\n"
                 + "🔥 倍倍倍プッシュ！！\n"
                 + "コスト：20,000コイン\n"
                 + "効果：10分間、DOUBLE UPの当選倍率を2.4倍に変更\n\n"
                 + "💣 Mines安全週間1\n"
                 + "コスト：発動者の所持コインの20%\n"
                  + "効果：1時間、ユーザーごとに最大15回まで\n"
                  + "      爆弾を1個減少（4個→3個）\n\n"
                 + "💣 Mines安全週間2\n"
                 + "コスト：発動者の所持コインの40%\n"
                  + "効果：1時間、ユーザーごとに最大10回まで\n"
                  + "      爆弾を2個減少（4個→2個）\n\n"
                 + "💣 Mines安全週間3\n"
                 + "コスト：発動者の所持コインの95%\n"
                  + "効果：1時間、ユーザーごとに最大5回まで\n"
                  + "      爆弾を3個減少（4個→1個）\n\n"
                 + "💎 生きるか死ぬか\n"
                 + "コスト：発動者の所持コインの50%\n"
                 + "効果：次の1ゲーム限定。勝利時は予定払い戻しが5倍、\n"
                 + "      敗北時はゲーム後の所持コインの50%を失う\n"
                + "```\n"
                + "通常のサーバーイベントは同時に1つだけ発動できます。\n"
                + "「生きるか死ぬか」は個人イベントのため、サーバーイベントと同時に発動できます。\n"
                + "発動コストの割合は、発動者またはサーバー内TOP10の所持コインを基準に計算します。"),
            ["admin"] = ("⚙️ 管理・設定", ButtonStyle.Secondary,
                "`/rolechannel`\n"
                 + "ロール解除通知の投稿先を、実行したチャンネルに設定します。\n"
                + "・管理者のみ利用できます。\n"
                 + "・未設定の状態で実行すると、ロール解除通知が有効になります。\n"
                 + "・設定済みの状態で再実行すると、ロール解除通知が無効になります。\n\n"
                + "AI、誕生日、VCログの各通知・会話チャンネル設定は、それぞれのカテゴリから確認できます。\n\n"
                + "`主な制限`\n"
                + "・サーバー専用コマンドはDMで実行できません。\n"
                + "・ベットや送金には残高が必要です。\n"
                + "・入力値や日時形式が不正な場合はエラーになります。\n"
                + "・ゲーム中は同じゲームを重複して開始できません。\n"
                + "・ゲームのボタンを操作できるのは開始者本人です。\n"
                + "・外部サービス障害時はAI機能を利用できません。")
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
