using System;
using System.Linq;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services;
using Xunit;

namespace TacoDiscordBot.Tests;

public sealed class ReversiServiceTests
{
    [Fact]
    public void 初期盤面は中央に4石あり黒の合法手が4個ある()
    {
        var service = new ReversiService();
        var game = service.Start(1, 2, 10);
        service.Join(game.GameId, 10);
        service.Join(game.GameId, 20);

        Assert.Equal(ReversiGameStatus.Playing, game.Status);
        Assert.Equal(4, game.Count(ReversiStone.Black) + game.Count(ReversiStone.White));
        Assert.Equal(4, game.ValidMoves.Count);
        Assert.Equal(4, game.NumberedMoves.Count);
    }

    [Fact]
    public void 番号付き合法手を選ぶと石を置いて相手の石を反転する()
    {
        var service = new ReversiService();
        var game = service.Start(1, 2, 10);
        service.Join(game.GameId, 10);
        service.Join(game.GameId, 20);
        var move = game.NumberedMoves.First();
        var version = game.BoardVersion;

        Assert.True(service.Play(game.GameId, 10, move, version));
        Assert.Equal(ReversiStone.Black, game.Board[move.Row, move.Column]);
        Assert.Equal(ReversiStone.Black, game.Board[3, 3]);
        Assert.Equal(ReversiStone.White, game.CurrentPlayer);
        Assert.Equal(1, game.TurnNumber);
    }

    [Fact]
    public void 赤または緑の合法手を昇格すると番号グループへ移動する()
    {
        var service = new ReversiService();
        var game = service.Start(1, 2, 10);
        service.Join(game.GameId, 10);
        service.Join(game.GameId, 20);

        Assert.Empty(game.RedMoves);
        Assert.Empty(game.GreenMoves);
        Assert.False(service.Promote(game.GameId, 10, new ReversiMove(0, 0), game.BoardVersion));
    }

    [Fact]
    public void 古い盤面バージョンのボタンは拒否する()
    {
        var service = new ReversiService();
        var game = service.Start(1, 2, 10);
        service.Join(game.GameId, 10);
        service.Join(game.GameId, 20);
        var move = game.NumberedMoves.First();

        service.Play(game.GameId, 10, move, game.BoardVersion);
        Assert.Throws<InvalidOperationException>(() => service.Play(game.GameId, 20, game.NumberedMoves.First(), 0));
    }

    [Fact]
    public void 参加者が3人を超えて参加できない()
    {
        var service = new ReversiService();
        var game = service.Start(1, 2, 10);

        Assert.True(service.Join(game.GameId, 10));
        Assert.True(service.Join(game.GameId, 20));
        Assert.False(service.Join(game.GameId, 30));
    }

    [Fact]
    public void ゲーム終了時は石数の多い色が勝者になる()
    {
        Assert.Equal(ReversiStone.Black, ReversiGame.DetermineWinner(40, 24));
        Assert.Equal(ReversiStone.White, ReversiGame.DetermineWinner(24, 40));
        Assert.Equal(ReversiStone.Empty, ReversiGame.DetermineWinner(32, 32));
    }
}
