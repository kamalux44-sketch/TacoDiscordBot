using System;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.SlashCommands;

namespace TacoDiscordBot;

public static class BotHost
{
    public static DiscordClient Client { get; private set; }
    public static Services.VcLogger VcLogger { get; private set; }
    public static Services.VcRankingService VcRankingService { get; private set; }
    public static Services.BoManager BoManager { get; private set; }

    public static async Task RunAsync()
    {
        try
        {
            Console.WriteLine("[BotHost] RunAsync開始");

            var token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");

            if (string.IsNullOrWhiteSpace(token))
            {
                Console.WriteLine(Strings.EnvTokenMissing);
                return;
            }

            Console.WriteLine("[BotHost] Discord token確認OK");

            Client = new DiscordClient(new DiscordConfiguration
            {
                Token = token,
                TokenType = TokenType.Bot,
                Intents =
                    DiscordIntents.Guilds |
                    DiscordIntents.GuildMessages |
                    DiscordIntents.GuildVoiceStates |
                    DiscordIntents.GuildMembers
            });

            Console.WriteLine("[BotHost] DiscordClient作成完了");

            // DB 環境から接続情報を構築する
            Repository.VcLogRepository vclogRepo = null;
            Repository.VcRankingRepository vrankRepo = null;
            Repository.BoRepository boRepo = null;

            var host = Environment.GetEnvironmentVariable("PGHOST");
            if (!string.IsNullOrWhiteSpace(host))
            {
                try
                {
                    var port = Environment.GetEnvironmentVariable("PGPORT") ?? Strings.DefaultDBPPort;
                    var db = Environment.GetEnvironmentVariable("PGDATABASE") ?? Strings.DefaultDBName;
                    var user = Environment.GetEnvironmentVariable("PGUSER");
                    var pass = Environment.GetEnvironmentVariable("PGPASSWORD");
                    var ssl = Environment.GetEnvironmentVariable("PGSSLMODE");

                    var parts = new System.Collections.Generic.List<string>
                    {
                        $"Host={host}",
                        $"Port={port}",
                        $"Database={db}"
                    };
                    if (!string.IsNullOrWhiteSpace(user)) parts.Add($"Username={user}");
                    if (!string.IsNullOrWhiteSpace(pass)) parts.Add($"Password={pass}");
                    if (!string.IsNullOrWhiteSpace(ssl)) parts.Add($"SslMode={ssl}");

                    var conn = string.Join(";", parts);
                    var baseRepo = new Repository.BaseRepository(conn, s => Console.WriteLine($"[DB] {s}"));

                    if (baseRepo.IsProviderAvailable())
                    {
                        Console.WriteLine("[BotHost] DB ドライバ確認 OK");

                        // DI を使ってリポジトリのインスタンスを作成
                        vclogRepo = new Repository.VcLogRepository(baseRepo);
                        vrankRepo = new Repository.VcRankingRepository(baseRepo);
                        boRepo = new Repository.BoRepository(baseRepo);

                        // すべてのリポジトリについてテーブルの存在確認と作成を行う
                        try
                        {
                            Console.WriteLine("[BotHost] DB テーブル確認・作成開始");
                            vclogRepo.EnsureTableExistsAsync().GetAwaiter().GetResult();
                            vrankRepo.EnsureTableExistsAsync().GetAwaiter().GetResult();
                            boRepo.EnsureTablesExistAsync().GetAwaiter().GetResult();
                            Console.WriteLine("[BotHost] DB テーブル確認・作成完了");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[BotHost] DB テーブル作成中にエラーが発生しました");
                            Console.WriteLine(ex.ToString());
                        }
                    }
                    else
                    {
                        Console.WriteLine("[BotHost] DB ドライバが見つかりません。Postgres 機能は無効になります。");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[BotHost] DB 初期化エラー");
                    Console.WriteLine(ex.ToString());
                }
            }

            // リポジトリ（任意）を注入してサービスを作成
            VcLogger = new Services.VcLogger(vclogRepo, vrankRepo);
            Console.WriteLine("[BotHost] VcLogger作成完了");

            BoManager = new Services.BoManager(Client, boRepo);
            Console.WriteLine("[BotHost] BoManager作成完了");

            // ランキングサービスを作成（DB 未構成の場合は何もしません）
            VcRankingService = new Services.VcRankingService();
            Console.WriteLine("[BotHost] VcRankingService作成完了");

            Client.VoiceStateUpdated +=
                VcLogger.HandleVoiceStateUpdated;
            // メッセージ送信とは独立してランキングの永続化を行う
            Client.VoiceStateUpdated +=
                VcRankingService.HandleVoiceStateUpdated;

            Client.ComponentInteractionCreated +=
                BoManager.HandleComponentInteraction;

            Console.WriteLine("[BotHost] イベント登録完了");

            var slash = Client.UseSlashCommands();

            Console.WriteLine("[BotHost] SlashCommandsExtension作成完了");

            slash.RegisterCommands<Commands.VcLogCommands>();
            Console.WriteLine("[BotHost] VcLogCommands登録完了");

            slash.RegisterCommands<Commands.VcRankingCommands>();
            Console.WriteLine("[BotHost] VcRankingCommands登録完了");

            slash.RegisterCommands<Commands.BoCommands>();

            Console.WriteLine("[BotHost] BoCommands登録完了");

            slash.RegisterCommands<Commands.DeadlineCommands>();

            Console.WriteLine("[BotHost] DeadlineCommands登録完了");

            Console.WriteLine("[BotHost] Discordへ接続開始");

            await Client.ConnectAsync();

            Console.WriteLine("[BotHost] Discord接続完了");

            // Botを終了させないために待機
            await Task.Delay(Timeout.Infinite);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[BotHost] 致命的な例外が発生しました");
            Console.WriteLine(ex.ToString());

            throw;
        }
    }
}
