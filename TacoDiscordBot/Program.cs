using System;
using System.Threading.Tasks;

namespace TacoDiscordBot;

public static class Program
{
    // Bot のエントリーポイントとしてホストを起動します。
    public static async Task Main()
    {
        try
        {
            Console.WriteLine("[Program] Bot起動");

            await BotHost.RunAsync();

            Console.WriteLine("[Program] BotHost.RunAsync終了");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Program] 致命的な例外");
            Console.WriteLine(ex.ToString());

            throw;
        }
    }
}
