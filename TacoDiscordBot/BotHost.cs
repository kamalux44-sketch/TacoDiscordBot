using DSharpPlus;
using DSharpPlus.SlashCommands;
using Microsoft.Extensions.Logging;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services.Interface;
using TacoDiscordBot.Util;

namespace TacoDiscordBot;

public static class BotHost
{
    public static DiscordClient Client { get; private set; }

    public static IVcLogService VcLogger { get; private set; }

    public static IVcRankingService VcRankingService { get; private set; }

    public static IBoService BoManager { get; private set; }

    public static IAiChannelService AiChannelService { get; private set; }

    public static IAiService AiService { get; private set; }

    public static Services.BirthdayService BirthdayService { get; private set; }

    public static Services.SlotService SlotService { get; private set; }

    public static Services.CoinService CoinService { get; private set; }

    public static Services.BlackjackService BlackjackService { get; private set; }

    public static Services.PokerService PokerService { get; private set; }

    public static Services.RouletteService RouletteService { get; private set; }

    public static Services.DoubleUpService DoubleUpService { get; private set; }

    public static Services.MinesService MinesService { get; private set; }

    public static Services.LastChanceService LastChanceService { get; private set; }

    public static Services.ReversiService ReversiService { get; private set; }

    public static Services.ReversiBetService ReversiBetService { get; private set; }

    public static Services.VcExchangeService VcExchangeService { get; private set; }

    public static Services.RoleService RoleService { get; private set; }

    public static Services.EventManager EventManager { get; private set; }

    public static async Task RunAsync()
    {
        try
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
                builder
                    .SetMinimumLevel(LogLevel.Information)
                    .AddSimpleConsole(options => options.SingleLine = true)
            );
            Logger.Configure(loggerFactory);

            Logger.Info("BotHost: RunAsync 開始");

            var token = Environment.GetEnvironmentVariable(Strings.EnvDiscordToken);

            if (string.IsNullOrWhiteSpace(token))
            {
                Logger.Info("BotHost: {Message}", Strings.EnvTokenMissing);

                return;
            }

            Logger.Info("BotHost: Discord token 確認 OK");
            var key = Environment.GetEnvironmentVariable(Strings.EnvGeminiApiKey);

            Logger.Info(
                "BotHost: GEMINI_API_KEY 設定状態={ApiKeyState} 長さ={ApiKeyLength}",
                string.IsNullOrEmpty(key) ? "NOT SET" : "OK",
                key?.Length ?? 0
            );

            Client = new DiscordClient(
                new DiscordConfiguration
                {
                    Token = token,
                    TokenType = TokenType.Bot,
                    Intents =
                        DiscordIntents.Guilds
                        | DiscordIntents.GuildMessages
                        | DiscordIntents.MessageContents
                        | DiscordIntents.GuildVoiceStates
                        | DiscordIntents.GuildMembers,
                }
            );

            Logger.Info("BotHost: DiscordClient 作成完了");

            // DB 環境から接続情報を構築する
            Repository.VcLogRepository vclogRepo = null;
            Repository.VcRankingRepository vrankRepo = null;
            Repository.BoRepository boRepo = null;
            Repository.AiTalkRepository aiRepo = null;
            Repository.BirthdayRepository birthdayRepo = null;
            Repository.SlotRepository slotRepo = null;
            Repository.UserDataRepository userDataRepo = null;
            Repository.VcExchangeRepository vcExchangeRepo = null;
            Repository.AchievementRepository achievementRepo = null;
            Repository.ServerEventRepository serverEventRepo = null;
            Repository.PokerRepository pokerRepo = null;
            var host = Environment.GetEnvironmentVariable(Strings.EnvPgHost);

            if (!string.IsNullOrWhiteSpace(host))
            {
                try
                {
                    var port =
                        Environment.GetEnvironmentVariable(Strings.EnvPgPort) ?? Strings.DefaultDBPPort;

                    var db =
                        Environment.GetEnvironmentVariable(Strings.EnvPgDatabase) ?? Strings.DefaultDBName;

                    var user = Environment.GetEnvironmentVariable(Strings.EnvPgUser);

                    var pass = Environment.GetEnvironmentVariable(Strings.EnvPgPassword);

                    var ssl = Environment.GetEnvironmentVariable(Strings.EnvPgSslMode);

                    var parts = new System.Collections.Generic.List<string>
                    {
                        $"Host={host}",
                        $"Port={port}",
                        $"Database={db}",
                    };

                    if (!string.IsNullOrWhiteSpace(user))
                    {
                        parts.Add($"Username={user}");
                    }

                    if (!string.IsNullOrWhiteSpace(pass))
                    {
                        parts.Add($"Password={pass}");
                    }

                    if (!string.IsNullOrWhiteSpace(ssl))
                    {
                        parts.Add($"SslMode={ssl}");
                    }

                    var conn = string.Join(";", parts);

                    var baseRepo = new Repository.BaseRepository(conn);

                    if (baseRepo.IsProviderAvailable())
                    {
                        Logger.Info("BotHost: DB ドライバ確認 OK");

                        // DI を使ってリポジトリのインスタンスを作成
                        vclogRepo = new Repository.VcLogRepository(baseRepo);

                        vrankRepo = new Repository.VcRankingRepository(baseRepo);

                        boRepo = new Repository.BoRepository(baseRepo);

                        birthdayRepo = new Repository.BirthdayRepository(baseRepo);
                        slotRepo = new Repository.SlotRepository(baseRepo);
                        userDataRepo = new Repository.UserDataRepository(baseRepo);
                        vcExchangeRepo = new Repository.VcExchangeRepository(baseRepo);
                        achievementRepo = new Repository.AchievementRepository(baseRepo);
                        serverEventRepo = new Repository.ServerEventRepository(baseRepo);
                        pokerRepo = new Repository.PokerRepository(baseRepo);
                        // すべてのリポジトリについて
                        // テーブルの存在確認と作成を行う
                        try
                        {
                            Logger.Info("BotHost: DB テーブル確認・作成開始");

                            vclogRepo.EnsureTableExistsAsync().GetAwaiter().GetResult();

                            vrankRepo.EnsureTableExistsAsync().GetAwaiter().GetResult();

                            vrankRepo.ResetOpenSessionsAtStartupAsync(DateTime.UtcNow).GetAwaiter().GetResult();

                            boRepo.EnsureTablesExistAsync().GetAwaiter().GetResult();

                            // AI 会話ターゲットテーブル
                            aiRepo = new Repository.AiTalkRepository(baseRepo);

                            aiRepo.EnsureTableExistsAsync().GetAwaiter().GetResult();

                            birthdayRepo.EnsureTablesExistAsync().GetAwaiter().GetResult();

                            slotRepo.EnsureTablesExistAsync().GetAwaiter().GetResult();

                            userDataRepo.EnsureTablesExistAsync().GetAwaiter().GetResult();

                            vcExchangeRepo.EnsureTablesExistAsync().GetAwaiter().GetResult();

                            achievementRepo.EnsureTablesExistAsync().GetAwaiter().GetResult();

                            serverEventRepo.EnsureTablesExistAsync().GetAwaiter().GetResult();

                            pokerRepo.EnsureTablesExistAsync().GetAwaiter().GetResult();

                            Logger.Info("BotHost: DB テーブル確認・作成完了");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, "BotHost: DB テーブル作成中にエラーが発生");
                        }
                    }
                    else
                    {
                        Logger.Info(
                            "[BotHost] DB ドライバが見つかりません。"
                                + "Postgres 機能は無効になります。"
                        );
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "BotHost: DB 初期化エラー");
                }
            }

            // リポジトリ（任意）を注入してサービスを作成
            VcLogger = new Services.VcLogService(vclogRepo, vrankRepo);

            Logger.Info("BotHost: VcLogger 作成完了");

            BoManager = new Services.BoService(Client, boRepo);

            Logger.Info("BotHost: BoManager 作成完了");

            // ランキングサービスを作成
            // DB 未構成の場合は何もしません
            VcRankingService = new Services.VcRankingService();

            Logger.Info("BotHost: VcRankingService 作成完了");

            // AI チャンネル管理サービスを作成
            // DB 未構成でも動作します
            AiChannelService = new Services.AiChannelService(aiRepo);

            Logger.Info("BotHost: AiChannelService 作成完了");

            // AI サービスを作成
            // チャンネルサービスを注入
            AiService = new Services.AIService(Client, AiChannelService);

            Logger.Info("BotHost: AIService 作成完了");

            BirthdayService = birthdayRepo == null
                ? null
                : new Services.BirthdayService(Client, birthdayRepo);

            BirthdayService?.StartDailyPosting();

            Logger.Info("BotHost: BirthdayService 作成完了");

            RoleService = achievementRepo == null
                ? null
                : userDataRepo == null
                    ? null
                    : new Services.RoleService(Client, achievementRepo, userDataRepo);

            CoinService = userDataRepo == null
                ? null
                : new Services.CoinService(userDataRepo, RoleService);

            EventManager = CoinService == null
                ? null
                : new Services.EventManager(CoinService, NotifyServerEventAsync, serverEventRepo);

            BlackjackService = CoinService == null
                ? null
                : new Services.BlackjackService(CoinService, RoleService, EventManager, userDataRepo);

            PokerService = CoinService == null
                ? null
                : new Services.PokerService(CoinService, pokerRepo);

            RouletteService = CoinService == null
                ? null
                : new Services.RouletteService(CoinService, eventManager: EventManager);

            DoubleUpService = CoinService == null
                ? null
                : new Services.DoubleUpService(CoinService, roleService: RoleService, eventManager: EventManager);

            MinesService = CoinService == null
                ? null
                : new Services.MinesService(CoinService, roleService: RoleService, eventManager: EventManager);

            LastChanceService = userDataRepo == null
                ? null
                : new Services.LastChanceService(userDataRepo, roleService: RoleService);

            ReversiService = new Services.ReversiService();
            ReversiBetService = CoinService == null
                ? null
                : new Services.ReversiBetService(CoinService);

            SlotService = slotRepo == null || CoinService == null
                ? null
                : new Services.SlotService(slotRepo, CoinService, RoleService, EventManager);

            VcExchangeService = vcExchangeRepo == null
                ? null
                : new Services.VcExchangeService(vcExchangeRepo, RoleService);

            Logger.Info("BotHost: SlotService 作成完了");

            // VC ログ
            Client.VoiceStateUpdated += VcLogger.HandleVoiceStateUpdated;

            // VC ランキング
            // メッセージ送信とは独立して
            // ランキングの永続化を行う
            Client.VoiceStateUpdated += VcRankingService.HandleVoiceStateUpdated;

            // BO コンポーネント
            Client.ComponentInteractionCreated += BoManager.HandleComponentInteraction;

            Client.ComponentInteractionCreated += Commands.BlackjackCommands.HandleComponentInteractionAsync;

            Client.ComponentInteractionCreated += Commands.PokerCommands.HandleComponentInteractionAsync;

            Client.ComponentInteractionCreated += Commands.RouletteCommands.HandleComponentInteractionAsync;

            Client.ComponentInteractionCreated += Commands.DoubleUpCommands.HandleComponentInteractionAsync;

            Client.ComponentInteractionCreated += Commands.MinesCommands.HandleComponentInteractionAsync;

            Client.ComponentInteractionCreated += Commands.LastChanceCommands.HandleComponentInteractionAsync;

            Client.ComponentInteractionCreated += Commands.ReversiCommands.HandleComponentInteractionAsync;

            Client.ComponentInteractionCreated += Commands.CoinCommands.HandleExchangeComponentInteractionAsync;

            Client.ComponentInteractionCreated += Commands.ServerEventCommands.HandleComponentInteractionAsync;

            Client.ComponentInteractionCreated += Commands.HelpCommands.HandleComponentInteractionAsync;

            // AI メッセージ
            Client.MessageCreated += AiService.HandleMessageCreated;

            Logger.Info("BotHost: イベント登録完了");

            var slash = Client.UseSlashCommands();

            Logger.Info("BotHost: SlashCommandsExtension 作成完了");

            slash.RegisterCommands<Commands.VcLogCommands>();

            Logger.Info("BotHost: VcLogCommands 登録完了");

            slash.RegisterCommands<Commands.VcRankingCommands>();

            Logger.Info("BotHost: VcRankingCommands 登録完了");

            slash.RegisterCommands<Commands.BoCommands>();

            Logger.Info("BotHost: BoCommands 登録完了");

            slash.RegisterCommands<Commands.AICommands>();

            Logger.Info("BotHost: AICommands 登録完了");

            slash.RegisterCommands<Commands.AIChannelCommands>();

            Logger.Info("BotHost: AIChannelCommands 登録完了");

            slash.RegisterCommands<Commands.DeadlineCommands>();

            Logger.Info("BotHost: DeadlineCommands 登録完了");

            slash.RegisterCommands<Commands.BirthdayCommands>();

            slash.RegisterCommands<Commands.RoleCommands>();

            Logger.Info("BotHost: BirthdayCommands 登録完了");

            slash.RegisterCommands<Commands.SlotCommands>();

            Logger.Info("BotHost: SlotCommands 登録完了");

            slash.RegisterCommands<Commands.BlackjackCommands>();

            slash.RegisterCommands<Commands.PokerCommands>();

            slash.RegisterCommands<Commands.RouletteCommands>();

            slash.RegisterCommands<Commands.DoubleUpCommands>();

            slash.RegisterCommands<Commands.MinesCommands>();

            slash.RegisterCommands<Commands.LastChanceCommands>();

            slash.RegisterCommands<Commands.ReversiCommands>();

            slash.RegisterCommands<Commands.CoinCommands>();

            slash.RegisterCommands<Commands.ServerEventCommands>();

            slash.RegisterCommands<Commands.HelpCommands>();

            Logger.Info("BotHost: Discord へ接続開始");

            await Client.ConnectAsync();

            Logger.Info("BotHost: Discord 接続完了");

            if (PokerService != null)
            {
                await PokerService.RestoreAsync();
                await Commands.PokerCommands.ResyncRestoredGamesAsync(Client, PokerService);
                Logger.Info("BotHost: Poker卓の復元完了");
            }

            if (RoleService != null)
            {
                await RoleService.InitializeRolesAsync();
                Logger.Info("BotHost: 実績ロール初期化完了");
            }

            if (EventManager != null)
                await EventManager.RestoreAsync();
            EventManager?.StartMonitoring();

            // Botを終了させないために待機
            await Task.Delay(Timeout.Infinite);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "BotHost: 致命的な例外が発生");

            throw;
        }
    }

    private static async Task NotifyServerEventAsync(ServerEvent serverEvent, bool started)
    {
        if (RoleService == null)
            return;

        var channelId = await RoleService.GetRoleNotificationChannelAsync(serverEvent.GuildId);
        if (!channelId.HasValue)
            return;

        var channel = await Client.GetChannelAsync(channelId.Value);
        var definition = Services.EventManager.Definitions
            .First(item => item.Type == serverEvent.Type);
        var message = started
            ? $"📢 サーバーイベント発令！\n\n{definition.Name}\n\n発令者: <@{serverEvent.ActivatorId}>\n\n効果:\n{definition.Description}\n\n終了予定: {serverEvent.EndsAt.LocalDateTime:yyyy/MM/dd HH:mm:ss}"
            : $"📢 サーバーイベント終了\n\n{definition.Name}\n\nイベント効果が終了しました。";
        await channel.SendMessageAsync(message);
    }
}
