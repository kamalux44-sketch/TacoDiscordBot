using System.Collections.Generic;

namespace TacoDiscordBot.Models;

public sealed class BlackjackGame
{
    public BlackjackGame(ulong guildId, ulong userId, long originalBet, IReadOnlyList<BlackjackCard> playerCards, IReadOnlyList<BlackjackCard> dealerCards, IEnumerable<BlackjackCard> deck)
    {
        GuildId = guildId;
        UserId = userId;
        OriginalBet = originalBet;
        CurrentBet = originalBet;
        PlayerCards = new List<BlackjackCard>(playerCards);
        DealerCards = new List<BlackjackCard>(dealerCards);
        Deck = new Queue<BlackjackCard>(deck);
    }

    public ulong GuildId { get; }

    public ulong UserId { get; }
    public long OriginalBet { get; }
    public long CurrentBet { get; private set; }
    public List<BlackjackCard> PlayerCards { get; }
    public List<BlackjackCard> DealerCards { get; }
    public Queue<BlackjackCard> Deck { get; }
    public bool HasActed { get; set; }
    public bool IsDoubledDown { get; set; }
    public bool IsFinished { get; set; }

    public void DoubleBet()
    {
        CurrentBet = checked(CurrentBet + OriginalBet);
        IsDoubledDown = true;
    }
}
