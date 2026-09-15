using System.Collections.Generic;

namespace TacoDiscordBot.Models;

public sealed class BlackjackGame
{
    public BlackjackGame(ulong userId, long bet, IReadOnlyList<BlackjackCard> playerCards, IReadOnlyList<BlackjackCard> dealerCards, IEnumerable<BlackjackCard> deck)
    {
        UserId = userId;
        Bet = bet;
        PlayerCards = new List<BlackjackCard>(playerCards);
        DealerCards = new List<BlackjackCard>(dealerCards);
        Deck = new Queue<BlackjackCard>(deck);
    }

    public ulong UserId { get; }
    public long Bet { get; }
    public List<BlackjackCard> PlayerCards { get; }
    public List<BlackjackCard> DealerCards { get; }
    public Queue<BlackjackCard> Deck { get; }
    public bool IsFinished { get; set; }
}
