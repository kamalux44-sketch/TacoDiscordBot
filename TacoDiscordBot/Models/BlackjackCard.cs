namespace TacoDiscordBot.Models;

public sealed record BlackjackCard(string Rank, string Suit)
{
    public int BaseValue => Rank switch
    {
        "A" => 11,
        "J" or "Q" or "K" => 10,
        _ => int.Parse(Rank)
    };

    public override string ToString() => $"{Rank}{Suit}";
}
