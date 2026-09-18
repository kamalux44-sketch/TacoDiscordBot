using System;
using System.Collections.Generic;
using System.Linq;

namespace TacoDiscordBot.Models;

public enum ReversiStone
{
    Empty,
    Black,
    White
}

public enum ReversiGameStatus
{
    WaitingForPlayers,
    Playing,
    Finished,
    Cancelled
}

public readonly record struct ReversiMove(int Row, int Column)
{
    public int Id => Row * ReversiGame.BoardSize + Column;
}

public sealed class ReversiGame
{
    public const int BoardSize = 8;
    public const int MaximumMovesPerGroup = 11;

    private static readonly (int Row, int Column)[] Directions =
    [
        (-1, -1), (-1, 0), (-1, 1),
        (0, -1), (0, 1),
        (1, -1), (1, 0), (1, 1)
    ];

    public ReversiGame(string gameId, ulong guildId, ulong channelId, ulong creatorId)
    {
        GameId = gameId;
        GuildId = guildId;
        ChannelId = channelId;
        CreatorId = creatorId;
        Board = CreateInitialBoard();
        NumberedMoves = [];
        RedMoves = [];
        GreenMoves = [];
        Status = ReversiGameStatus.WaitingForPlayers;
    }

    public string GameId { get; }
    public ulong GuildId { get; }
    public ulong ChannelId { get; }
    public ulong CreatorId { get; }
    public ulong? PlayerBlack { get; private set; }
    public ulong? PlayerWhite { get; private set; }
    public ReversiStone[,] Board { get; }
    public ReversiStone CurrentPlayer { get; private set; } = ReversiStone.Black;
    public ReversiGameStatus Status { get; private set; }
    public int TurnNumber { get; private set; }
    public int BoardVersion { get; private set; }
    public ReversiStone Winner
        => Status != ReversiGameStatus.Finished
            ? ReversiStone.Empty
            : DetermineWinner(Count(ReversiStone.Black), Count(ReversiStone.White));

    public static ReversiStone DetermineWinner(int blackCount, int whiteCount)
        => blackCount == whiteCount
            ? ReversiStone.Empty
            : blackCount > whiteCount ? ReversiStone.Black : ReversiStone.White;
    public IReadOnlyList<ReversiMove> ValidMoves => validMoves;
    public IReadOnlyList<ReversiMove> NumberedMoves { get; private set; }
    public IReadOnlyList<ReversiMove> RedMoves { get; private set; }
    public IReadOnlyList<ReversiMove> GreenMoves { get; private set; }

    private List<ReversiMove> validMoves = [];

    public bool TryJoin(ulong userId)
    {
        if (Status != ReversiGameStatus.WaitingForPlayers || PlayerBlack == userId || PlayerWhite == userId)
            return false;

        if (!PlayerBlack.HasValue)
        {
            PlayerBlack = userId;
        }
        else if (!PlayerWhite.HasValue)
        {
            PlayerWhite = userId;
            Status = ReversiGameStatus.Playing;
            RefreshMoves();
        }
        else
        {
            return false;
        }

        return true;
    }

    public bool TryCancel()
    {
        if (Status is ReversiGameStatus.Finished or ReversiGameStatus.Cancelled)
            return false;

        Status = ReversiGameStatus.Cancelled;
        BoardVersion++;
        return true;
    }

    public bool IsCurrentPlayer(ulong userId)
        => CurrentPlayer == ReversiStone.Black ? PlayerBlack == userId : PlayerWhite == userId;

    public bool TryPromote(ReversiMove move, out ReversiMove displaced)
    {
        displaced = default;
        if (Status != ReversiGameStatus.Playing || !validMoves.Contains(move))
            return false;

        var source = RedMoves.Contains(move) ? RedMoves : GreenMoves.Contains(move) ? GreenMoves : null;
        if (source == null)
            return false;

        var numbered = NumberedMoves.ToList();
        if (numbered.Count >= MaximumMovesPerGroup)
        {
            displaced = numbered[0];
            numbered.RemoveAt(0);
            var displacedGroup = source == RedMoves ? RedMoves.ToList() : GreenMoves.ToList();
            displacedGroup.Add(displaced);
            SetGroups(numbered, source == RedMoves ? displacedGroup : RedMoves, source == GreenMoves ? displacedGroup : GreenMoves);
        }
        else
        {
            var red = RedMoves.Where(item => item != move).ToList();
            var green = GreenMoves.Where(item => item != move).ToList();
            numbered.Add(move);
            SetGroups(numbered, red, green);
        }

        BoardVersion++;
        return true;
    }

    public bool TryPlay(ReversiMove move)
    {
        if (Status != ReversiGameStatus.Playing || !NumberedMoves.Contains(move) || !validMoves.Contains(move))
            return false;

        var opponent = Opponent(CurrentPlayer);
        var captured = GetCapturedStones(move, CurrentPlayer, opponent);
        if (captured.Count == 0)
            return false;

        Board[move.Row, move.Column] = CurrentPlayer;
        foreach (var capturedMove in captured)
            Board[capturedMove.Row, capturedMove.Column] = CurrentPlayer;

        CurrentPlayer = opponent;
        TurnNumber++;
        BoardVersion++;
        RefreshMovesWithPass();
        return true;
    }

    public int Count(ReversiStone stone)
    {
        var count = 0;
        for (var row = 0; row < BoardSize; row++)
        for (var column = 0; column < BoardSize; column++)
            if (Board[row, column] == stone) count++;
        return count;
    }

    private void RefreshMovesWithPass()
    {
        RefreshMoves();
        if (validMoves.Count > 0)
            return;

        CurrentPlayer = Opponent(CurrentPlayer);
        RefreshMoves();
        if (validMoves.Count == 0)
            Status = ReversiGameStatus.Finished;
    }

    private void RefreshMoves()
    {
        validMoves = [];
        for (var row = 0; row < BoardSize; row++)
        for (var column = 0; column < BoardSize; column++)
        {
            var move = new ReversiMove(row, column);
            if (Board[row, column] == ReversiStone.Empty
                && GetCapturedStones(move, CurrentPlayer, Opponent(CurrentPlayer)).Count > 0)
                validMoves.Add(move);
        }

        SetGroups(
            validMoves.Take(MaximumMovesPerGroup),
            validMoves.Skip(MaximumMovesPerGroup).Take(MaximumMovesPerGroup),
            validMoves.Skip(MaximumMovesPerGroup * 2).Take(MaximumMovesPerGroup)
        );
    }

    private void SetGroups(IEnumerable<ReversiMove> numbered, IEnumerable<ReversiMove> red, IEnumerable<ReversiMove> green)
    {
        NumberedMoves = numbered.ToList();
        RedMoves = red.ToList();
        GreenMoves = green.ToList();
    }

    private List<ReversiMove> GetCapturedStones(ReversiMove move, ReversiStone own, ReversiStone opponent)
    {
        var captured = new List<ReversiMove>();
        foreach (var direction in Directions)
        {
            var line = new List<ReversiMove>();
            var row = move.Row + direction.Row;
            var column = move.Column + direction.Column;
            while (IsOnBoard(row, column) && Board[row, column] == opponent)
            {
                line.Add(new ReversiMove(row, column));
                row += direction.Row;
                column += direction.Column;
            }

            if (line.Count > 0 && IsOnBoard(row, column) && Board[row, column] == own)
                captured.AddRange(line);
        }
        return captured;
    }

    private static ReversiStone[,] CreateInitialBoard()
    {
        var board = new ReversiStone[BoardSize, BoardSize];
        board[3, 3] = ReversiStone.White;
        board[3, 4] = ReversiStone.Black;
        board[4, 3] = ReversiStone.Black;
        board[4, 4] = ReversiStone.White;
        return board;
    }

    private static ReversiStone Opponent(ReversiStone stone)
        => stone == ReversiStone.Black ? ReversiStone.White : ReversiStone.Black;

    private static bool IsOnBoard(int row, int column)
        => row >= 0 && row < BoardSize && column >= 0 && column < BoardSize;
}
