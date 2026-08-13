using System;

namespace TacoDiscordBot.Util;

public static class Logger
{
    public static void Info(string message)
    {
        try
        {
            Console.WriteLine($"[Logger] {message}");
        }
        catch
        {
            // ÉçÉOé∏îsÇÕñ≥éã
        }
    }

    public static void Error(Exception ex, string? context = null)
    {
        try
        {
            var ctx = string.IsNullOrEmpty(context) ? string.Empty : $" {context}";
            Console.WriteLine($"[Logger] ERROR{ctx}: {ex}");
        }
        catch
        {
            // ÉçÉOé∏îsÇÕñ≥éã
        }
    }
}
