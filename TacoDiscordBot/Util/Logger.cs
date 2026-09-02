using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TacoDiscordBot.Util;

public static class Logger
{
    private static ILogger _logger = NullLogger.Instance;

    public static void Configure(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _logger = loggerFactory.CreateLogger("TacoDiscordBot");
    }

    public static void Info(string message, params object?[] args)
    {
        _logger.LogInformation(message, args);
    }

    public static void Error(Exception ex, string? context = null, params object?[] args)
    {
        _logger.LogError(ex, context ?? string.Empty, args);
    }
}
