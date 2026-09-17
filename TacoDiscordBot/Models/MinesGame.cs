using System;
using System.Collections.Generic;
using System.Linq;

namespace TacoDiscordBot.Models;

public sealed class MinesGame
{
    public const int BoardSize = 20;
    public const int BombCount = 4;
    public const int RowCount = 4;
    public const int ColumnCount = 5;

    public MinesGame(ulong guildId, ulong userId, long bet, IEnumerable<int> bombs)
    {
        GuildId = guildId;
        UserId = userId;
        Bet = bet;
        Bombs = new HashSet<int>(bombs);
        Opened = new HashSet<int>();
        State = MinesGameState.Playing;
    }

    public ulong GuildId { get; }
    public ulong UserId { get; }
    public long Bet { get; }
    public HashSet<int> Bombs { get; }
    public HashSet<int> Opened { get; }
    public MinesGameState State { get; set; }

    public int SafeOpenedCount => Opened.Count(index => !Bombs.Contains(index));

    public double Multiplier => MinesRules.GetMultiplier(SafeOpenedCount);

    public long CurrentAmount => State == MinesGameState.Lost
        ? 0
        : checked((long)Math.Floor(Bet * Multiplier));
}

public enum MinesGameState
{
    Playing,
    Lost,
    CashedOut,
    Won
}

public static class MinesRules
{
    private static readonly double[] Multipliers =
    {
        1.0, 1.0, 1.5, 2.0, 2.5, 3.5, 5.0, 7.0, 10.0,
        14.0, 22.0, 35.0, 60.0, 110.0, 250.0, 600.0, 5000.0
    };

    public static double GetMultiplier(int safeOpenedCount)
    {
        if (safeOpenedCount < 0)
            throw new ArgumentOutOfRangeException(nameof(safeOpenedCount));

        return safeOpenedCount < Multipliers.Length ? Multipliers[safeOpenedCount] : Multipliers[^1];
    }
}

public sealed class MinesResult
{
    public MinesResult(MinesGame game, string message)
    {
        Game = game;
        Message = message;
    }

    public MinesGame Game { get; }
    public string Message { get; }
    public bool IsFinished => Game.State != MinesGameState.Playing;
}
