namespace TacoDiscordBot.Models;

public enum PokerPhase
{
    Waiting,
    BetRound1,
    Exchange,
    BetRound2,
    Finished
}

public enum PokerAction
{
    Check,
    Bet,
    Call,
    Raise,
    Fold,
    AllIn
}

public sealed class PokerPlayer
{
    public PokerPlayer(ulong userId, string displayName, long chips)
    {
        UserId = userId;
        DisplayName = displayName;
        Chips = chips;
    }

    public ulong UserId { get; }
    public string DisplayName { get; }
    public long Chips { get; set; }
    public List<PokerCard> Hand { get; } = [];
    public bool Folded { get; set; }
    public bool Exchanged { get; set; }
    public long CurrentBet { get; set; }
    public bool AllIn { get; set; }
    public List<string> ActionHistory { get; } = [];
    public ulong? DirectMessageChannelId { get; set; }
    public ulong? DirectPublicMessageId { get; set; }
    public ulong? DirectHandMessageId { get; set; }
}

public sealed class PokerGame
{
    public PokerGame(string tableId, ulong guildId, ulong creatorId, long coinRate, ulong channelId)
    {
        TableId = tableId;
        GuildId = guildId;
        CreatorId = creatorId;
        CoinRate = coinRate;
        ChannelId = channelId;
    }

    public string TableId { get; }
    public ulong GuildId { get; }
    public ulong CreatorId { get; }
    public ulong ChannelId { get; }
    public ulong? PublicMessageId { get; set; }
    public long CoinRate { get; }
    public long Pot { get; set; }
    public long CurrentBet { get; set; }
    public int BetRound { get; set; } = 1;
    public PokerPhase Phase { get; set; } = PokerPhase.Waiting;
    public int CurrentPlayerIndex { get; set; }
    public List<PokerPlayer> Players { get; } = [];
    public Queue<PokerCard> Deck { get; set; } = new();
    public List<string> ActionHistory { get; } = [];
    public HashSet<ulong> BetRoundActedPlayerIds { get; } = [];
    public string? WinnerText { get; set; }
    public bool Settled { get; set; }
}
