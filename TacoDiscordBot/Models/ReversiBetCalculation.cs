using System;

namespace TacoDiscordBot.Models;

public sealed record ReversiBetCalculation(
    decimal Multiplier,
    long AdditionalLoss,
    long Payout,
    long WinnerNet,
    long LoserNet
);

public static class ReversiBetCalculator
{
    private const decimal PerfectWinMultiplier = 10m;
    private const decimal MaximumRegularMultiplier = 8m;

    private static readonly (decimal Ratio, decimal Multiplier)[] MultiplierAnchors =
    [
        (33m / 31m, 1.0m),
        (36m / 28m, 1.2m),
        (40m / 24m, 1.8m),
        (44m / 20m, 2.4m),
        (48m / 16m, 3.4m),
        (52m / 12m, 4.7m),
        (56m / 8m, 6.2m),
        (58m / 6m, 7.0m),
        (60m / 4m, 8.0m),
        (62m / 2m, 8.0m)
    ];

    public static ReversiBetCalculation Calculate(long betAmount, int winnerStones, int loserStones)
    {
        if (betAmount <= 0)
            throw new ArgumentOutOfRangeException(nameof(betAmount));
        if (winnerStones < 0 || loserStones < 0 || winnerStones == 0)
            throw new ArgumentOutOfRangeException(nameof(winnerStones));

        var multiplier = loserStones == 0
            ? PerfectWinMultiplier
            : CalculateRegularMultiplier(winnerStones, loserStones);
        var field = checked(betAmount * 2);
        var additionalLoss = checked((long)decimal.Truncate(field * (multiplier - 1m)));
        var payout = checked(field + additionalLoss);
        var winnerNet = checked(payout - betAmount);
        var loserNet = checked(-betAmount - additionalLoss);
        return new ReversiBetCalculation(multiplier, additionalLoss, payout, winnerNet, loserNet);
    }

    private static decimal CalculateRegularMultiplier(int winnerStones, int loserStones)
    {
        var ratio = (decimal)winnerStones / loserStones;
        if (ratio <= MultiplierAnchors[0].Ratio)
            return MultiplierAnchors[0].Multiplier;
        if (ratio >= MultiplierAnchors[^1].Ratio)
            return MaximumRegularMultiplier;

        for (var index = 1; index < MultiplierAnchors.Length; index++)
        {
            var upper = MultiplierAnchors[index];
            if (ratio > upper.Ratio)
                continue;

            var lower = MultiplierAnchors[index - 1];
            var position = (ratio - lower.Ratio) / (upper.Ratio - lower.Ratio);
            var interpolated = lower.Multiplier + (upper.Multiplier - lower.Multiplier) * position;
            return decimal.Truncate(interpolated * 10m) / 10m;
        }

        return MaximumRegularMultiplier;
    }
}