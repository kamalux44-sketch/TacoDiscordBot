using TacoDiscordBot.Models;

namespace TacoDiscordBot.Services;

public enum PokerHandCategory
{
    HighCard,
    OnePair,
    TwoPair,
    ThreeOfAKind,
    Straight,
    Flush,
    FullHouse,
    FourOfAKind,
    StraightFlush,
    RoyalFlush
}

public sealed record PokerHandEvaluation(PokerHandCategory Category, IReadOnlyList<int> TieBreakValues)
    : IComparable<PokerHandEvaluation>
{
    public int CompareTo(PokerHandEvaluation? other)
    {
        if (other is null)
            return 1;

        var categoryComparison = Category.CompareTo(other.Category);
        if (categoryComparison != 0)
            return categoryComparison;

        for (var index = 0; index < Math.Min(TieBreakValues.Count, other.TieBreakValues.Count); index++)
        {
            var comparison = TieBreakValues[index].CompareTo(other.TieBreakValues[index]);
            if (comparison != 0)
                return comparison;
        }

        return TieBreakValues.Count.CompareTo(other.TieBreakValues.Count);
    }

    public string CategoryDisplayName => Category switch
    {
        PokerHandCategory.RoyalFlush => "ロイヤルフラッシュ",
        PokerHandCategory.StraightFlush => "ストレートフラッシュ",
        PokerHandCategory.FourOfAKind => "フォーカード",
        PokerHandCategory.FullHouse => "フルハウス",
        PokerHandCategory.Flush => "フラッシュ",
        PokerHandCategory.Straight => "ストレート",
        PokerHandCategory.ThreeOfAKind => "スリーカード",
        PokerHandCategory.TwoPair => "ツーペア",
        PokerHandCategory.OnePair => "ワンペア",
        _ => "ハイカード"
    };
}

public static class PokerHandEvaluator
{
    public static PokerHandEvaluation Evaluate(IReadOnlyList<PokerCard> hand)
    {
        if (hand is null || hand.Count != 5)
            throw new ArgumentException("ポーカーの手札は5枚必要です。", nameof(hand));

        var groups = hand
            .GroupBy(card => (int)card.Rank)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key)
            .ToList();
        var values = hand.Select(card => (int)card.Rank).OrderByDescending(value => value).ToList();
        var flush = hand.Select(card => card.Suit).Distinct().Count() == 1;
        var straightHigh = GetStraightHigh(values);

        if (flush && straightHigh.HasValue)
        {
            return new PokerHandEvaluation(
                straightHigh == (int)PokerRank.Ace ? PokerHandCategory.RoyalFlush : PokerHandCategory.StraightFlush,
                [straightHigh.Value]
            );
        }

        if (groups[0].Count() == 4)
            return new PokerHandEvaluation(PokerHandCategory.FourOfAKind, [groups[0].Key, groups[1].Key]);

        if (groups[0].Count() == 3 && groups[1].Count() == 2)
            return new PokerHandEvaluation(PokerHandCategory.FullHouse, [groups[0].Key, groups[1].Key]);

        if (flush)
            return new PokerHandEvaluation(PokerHandCategory.Flush, values);

        if (straightHigh.HasValue)
            return new PokerHandEvaluation(PokerHandCategory.Straight, [straightHigh.Value]);

        if (groups[0].Count() == 3)
            return new PokerHandEvaluation(PokerHandCategory.ThreeOfAKind, [groups[0].Key, groups[1].Key, groups[2].Key]);

        if (groups[0].Count() == 2 && groups[1].Count() == 2)
            return new PokerHandEvaluation(PokerHandCategory.TwoPair, [groups[0].Key, groups[1].Key, groups[2].Key]);

        if (groups[0].Count() == 2)
            return new PokerHandEvaluation(PokerHandCategory.OnePair, [groups[0].Key, groups[1].Key, groups[2].Key, groups[3].Key]);

        return new PokerHandEvaluation(PokerHandCategory.HighCard, values);
    }

    private static int? GetStraightHigh(IReadOnlyList<int> values)
    {
        var distinct = values.Distinct().OrderBy(value => value).ToList();
        if (distinct.Count != 5)
            return null;

        if (distinct.SequenceEqual([2, 3, 4, 5, 14]))
            return 5;

        return distinct[^1] - distinct[0] == 4 ? distinct[^1] : null;
    }
}
