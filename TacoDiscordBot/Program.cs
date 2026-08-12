// See https://aka.ms/new-console-template for more information
using System;
using System.Threading.Tasks;

namespace TacoDiscordBot;

public static class Program
{
    public static async Task Main()
    {
        await BotHost.RunAsync();
    }
}
