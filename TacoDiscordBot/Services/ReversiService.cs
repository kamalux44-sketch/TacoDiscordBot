using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TacoDiscordBot.Models;

namespace TacoDiscordBot.Services;

public sealed class ReversiService
{
    private readonly object syncRoot = new();
    private readonly Dictionary<string, ReversiGame> games = [];

    public ReversiGame Start(ulong guildId, ulong channelId, ulong creatorId, long betAmount = 0)
    {
        if (betAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(betAmount));
        if (betAmount > 0)
            _ = checked(betAmount * 20);
        lock (syncRoot)
        {
            var game = new ReversiGame(Guid.NewGuid().ToString("N"), guildId, channelId, creatorId, betAmount);
            games.Add(game.GameId, game);
            return game;
        }
    }

    public async Task<bool> JoinAsync(string gameId, ulong userId, ReversiBetService? betService)
    {
        ReversiGame game;
        lock (syncRoot)
        {
            game = GetRequired(gameId);
            if (!game.BetEnabled || game.PlayerWhite.HasValue || !game.PlayerBlack.HasValue)
                return game.TryJoin(userId);
        }

        if (!await betService!.TryStartAsync(game, userId))
            return false;

        lock (syncRoot)
            return game.TryJoin(userId);
    }

    public async Task<ReversiBetCalculation?> PlayAndSettleAsync(
        string gameId,
        ulong userId,
        ReversiMove move,
        int boardVersion,
        ReversiBetService? betService
    )
    {
        bool finished;
        lock (syncRoot)
        {
            var game = GetRequired(gameId);
            ValidateTurnAndVersion(game, userId, boardVersion);
            game.TryPlay(move);
            finished = game.Status == ReversiGameStatus.Finished;
        }

        return finished && betService != null
            ? await betService.SettleAsync(GetRequired(gameId))
            : null;
    }

    public ReversiGame? Get(string gameId)
    {
        lock (syncRoot)
            return games.TryGetValue(gameId, out var game) ? game : null;
    }

    public bool Join(string gameId, ulong userId)
    {
        lock (syncRoot)
            return GetRequired(gameId).TryJoin(userId);
    }

    public bool Cancel(string gameId, ulong userId)
    {
        lock (syncRoot)
        {
            var game = GetRequired(gameId);
            if (game.CreatorId != userId)
                return false;
            return game.TryCancel();
        }
    }

    public bool Promote(string gameId, ulong userId, ReversiMove move, int boardVersion)
    {
        lock (syncRoot)
        {
            var game = GetRequired(gameId);
            ValidateTurnAndVersion(game, userId, boardVersion);
            return game.TryPromote(move, out _);
        }
    }

    public bool Play(string gameId, ulong userId, ReversiMove move, int boardVersion)
    {
        lock (syncRoot)
        {
            var game = GetRequired(gameId);
            ValidateTurnAndVersion(game, userId, boardVersion);
            return game.TryPlay(move);
        }
    }

    private ReversiGame GetRequired(string gameId)
    {
        if (!games.TryGetValue(gameId, out var game))
            throw new InvalidOperationException("リバーシゲームが見つかりません。");
        return game;
    }

    private static void ValidateTurnAndVersion(ReversiGame game, ulong userId, int boardVersion)
    {
        if (game.Status != ReversiGameStatus.Playing)
            throw new InvalidOperationException("このゲームは操作できません。");
        if (!game.IsCurrentPlayer(userId))
            throw new InvalidOperationException("現在は相手プレイヤーのターンです。");
        if (game.BoardVersion != boardVersion)
            throw new InvalidOperationException("このボタンは古いため使用できません。盤面を更新してください。");
    }
}
