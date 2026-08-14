using System;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.SlashCommands;

namespace TacoDiscordBot;

public static class BotHost
{
    public static DiscordClient Client { get; private set; }
    public static Services.VcLogger VcLogger { get; private set; }
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

            VcLogger = new Services.VcLogger();

            Console.WriteLine("[BotHost] VcLogger作成完了");

            BoManager = new Services.BoManager(Client);

            Console.WriteLine("[BotHost] BoManager作成完了");

            Client.VoiceStateUpdated +=
                VcLogger.HandleVoiceStateUpdated;

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
