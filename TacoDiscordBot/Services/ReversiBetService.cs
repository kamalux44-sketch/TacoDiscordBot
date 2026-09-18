using System;
using System.Threading.Tasks;
using TacoDiscordBot.Models;

namespace TacoDiscordBot.Services;

public sealed class ReversiBetService
{
    private readonly CoinService coinService;

    public ReversiBetService(CoinService coinService)
        => this.coinService = coinService ?? throw new ArgumentNullException(nameof(coinService));

    public Task<bool> TryStartAsync(ReversiGame game, ulong whitePlayerId)
    {
        if (!game.BetEnabled || !game.PlayerBlack.HasValue)
            return Task.FromResult(true);

        return coinService.TryStartReversiBetAsync(
            game.GuildId,
            game.PlayerBlack.Value,
            whitePlayerId,
            game.BetAmount
        );
    }

    public async Task<ReversiBetCalculation?> SettleAsync(ReversiGame game)
    {
        if (!game.BetEnabled || game.Status != ReversiGameStatus.Finished || !game.PlayerBlack.HasValue || !game.PlayerWhite.HasValue)
            return null;
        if (!game.TryBeginSettlement())
            return null;

        try
        {
            var winner = game.Winner;
            var blackStones = game.Count(ReversiStone.Black);
            var whiteStones = game.Count(ReversiStone.White);
            if (winner == ReversiStone.Empty)
            {
                var draw = new ReversiBetCalculation(1m, 0, game.FieldAmount, 0, 0);
                var drawSettled = await coinService.SettleReversiBetAsync(
                    game.GuildId,
                    game.PlayerBlack.Value,
                    game.PlayerWhite.Value,
                    null,
                    game.BetAmount,
                    0,
                    game.FieldAmount
                );
                if (!drawSettled)
                    throw new InvalidOperationException("ベット精算に失敗しました。");
                game.MarkSettlementCompleted(draw);
                return draw;
            }

            var winnerId = winner == ReversiStone.Black ? game.PlayerBlack.Value : game.PlayerWhite.Value;
            var calculation = ReversiBetCalculator.Calculate(
                game.BetAmount,
                winner == ReversiStone.Black ? blackStones : whiteStones,
                winner == ReversiStone.Black ? whiteStones : blackStones
            );
            var settled = await coinService.SettleReversiBetAsync(
                game.GuildId,
                game.PlayerBlack.Value,
                game.PlayerWhite.Value,
                winnerId,
                game.BetAmount,
                calculation.AdditionalLoss,
                calculation.Payout
            );
            if (!settled)
                throw new InvalidOperationException("ベット精算に必要なコインが不足しています。");

            game.MarkSettlementCompleted(calculation);
            return calculation;
        }
        catch
        {
            game.CancelSettlement();
            throw;
        }
    }
}