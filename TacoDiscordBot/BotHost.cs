using DSharpPlus;
using DSharpPlus.SlashCommands;
using Microsoft.Extensions.Logging;
using TacoDiscordBot.Services.Interface;
using TacoDiscordBot.Util;

namespace TacoDiscordBot;

public static class BotHost
{
    public static DiscordClient Client { get; private set; }

    public static IVcLogger VcLogger { get; private set; }

    public static IVcRankingService VcRankingService { get; private set; }

    public static IBoManager BoManager { get; private set; }

    public static IAiChannelService AiChannelService { get; private set; }

    public static IAiService AiService { get; private set; }

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

                        // すべてのリポジトリについて
                        // テーブルの存在確認と作成を行う
                        try
                        {
                            Logger.Info("BotHost: DB テーブル確認・作成開始");

                            vclogRepo.EnsureTableExistsAsync().GetAwaiter().GetResult();

                            vrankRepo.EnsureTableExistsAsync().GetAwaiter().GetResult();

                            boRepo.EnsureTablesExistAsync().GetAwaiter().GetResult();

                            // AI 会話ターゲットテーブル
                            aiRepo = new Repository.AiTalkRepository(baseRepo);

                            aiRepo.EnsureTableExistsAsync().GetAwaiter().GetResult();

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

            // VC ログ
            Client.VoiceStateUpdated += VcLogger.HandleVoiceStateUpdated;

            // VC ランキング
            // メッセージ送信とは独立して
            // ランキングの永続化を行う
            Client.VoiceStateUpdated += VcRankingService.HandleVoiceStateUpdated;

            // BO コンポーネント
            Client.ComponentInteractionCreated += BoManager.HandleComponentInteraction;

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

            Logger.Info("BotHost: Discord へ接続開始");

            await Client.ConnectAsync();

            Logger.Info("BotHost: Discord 接続完了");

            // Botを終了させないために待機
            await Task.Delay(Timeout.Infinite);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "BotHost: 致命的な例外が発生");

            throw;
        }
    }
}
