using System;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.SlashCommands;
// Interactivity extension removed to keep minimal dependencies. Add the package if needed later.

namespace TacoDiscordBot;

public static class BotHost
{
    public static DiscordClient Client { get; private set; }
    public static Services.VcLogger VcLogger { get; private set; }
    public static Services.BoManager BoManager { get; private set; }

    public static async Task RunAsync()
    {
        var token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");
        // 環境変数に Discord トークンが設定されていることを確認します。
        // 設定されていない場合は起動を中止します。
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.WriteLine(Strings.EnvTokenMissing);
            return;
        }

        Client = new DiscordClient(new DiscordConfiguration
        {
            Token = token,
            TokenType = TokenType.Bot,
            Intents = DiscordIntents.Guilds | DiscordIntents.GuildMessages | DiscordIntents.GuildVoiceStates | DiscordIntents.GuildMembers
        });

        // Interactivity is optional; if needed add the DSharpPlus.Interactivity package and enable it here.

        // initialize services
        VcLogger = new Services.VcLogger();
        BoManager = new Services.BoManager(Client);

        // wire events
        Client.VoiceStateUpdated += VcLogger.HandleVoiceStateUpdated;
        Client.ComponentInteractionCreated += BoManager.HandleComponentInteraction;

        var slash = Client.UseSlashCommands();
        slash.RegisterCommands<Commands.VcCommands>();
        slash.RegisterCommands<Commands.BoCommands>();

        await Client.ConnectAsync();
        await Task.Delay(-1);
    }
}
