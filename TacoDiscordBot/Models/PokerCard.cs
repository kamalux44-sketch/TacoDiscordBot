namespace TacoDiscordBot.Models;

public enum PokerSuit
{
    Spades,
    Hearts,
    Diamonds,
    Clubs
}

public enum PokerRank
{
    Two = 2,
    Three,
    Four,
    Five,
    Six,
    Seven,
    Eight,
    Nine,
    Ten,
    Jack,
    Queen,
    King,
    Ace
}

public readonly record struct PokerCard(PokerRank Rank, PokerSuit Suit)
{
    public override string ToString() => $"{Rank.ToDisplayString()}{Suit.ToDisplayString()}";
}

public static class PokerDisplayExtensions
{
    public static string ToDisplayString(this PokerRank rank) => rank switch
    {
        PokerRank.Ten => "10",
        PokerRank.Jack => "J",
        PokerRank.Queen => "Q",
        PokerRank.King => "K",
        PokerRank.Ace => "A",
        _ => ((int)rank).ToString()
    };

    public static string ToDisplayString(this PokerSuit suit) => suit switch
    {
        PokerSuit.Spades => "♠",
        PokerSuit.Hearts => "♥",
        PokerSuit.Diamonds => "♦",
        _ => "♣"
    };
}
