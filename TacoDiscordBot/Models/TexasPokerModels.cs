namespace TacoDiscordBot.Models;

public enum TexasPokerPhase
{
    Waiting,
    PreFlop,
    Flop,
    Turn,
    River,
    Showdown,
    Finished,
    Cancelled
}

public enum TexasPokerAction
{
    Check,
    Call,
    Bet,
    Raise,
    Fold
}

public sealed class TexasPokerPlayer
{
    public TexasPokerPlayer(ulong userId, string displayName)
    {
        UserId = userId;
        DisplayName = displayName;
        Chips = TexasPokerGame.InitialChips;
    }

    public ulong UserId { get; }
    public string DisplayName { get; }
    public List<PokerCard> HoleCards { get; } = [];
    public long Chips { get; set; }
    public long CurrentBet { get; set; }
    public bool IsFolded { get; set; }
    public bool IsAllIn { get; set; }
    public TexasPokerPlayerState State { get; set; } = TexasPokerPlayerState.Waiting;
}

public enum TexasPokerPlayerState
{
    Waiting,
    Acting,
    Called,
    Checked,
    Folded,
    AllIn,
    Winner,
    Loser
}

public sealed class TexasPokerGame
{
    public const int MinPlayers = 2;
    public const int MaxPlayers = 9;
    public const long InitialChips = 1000;
    public const long SmallBlind = 10;
    public const long BigBlind = 20;

    public TexasPokerGame(string gameId, ulong guildId, ulong hostId, ulong channelId)
    {
        GameId = gameId;
        GuildId = guildId;
        HostId = hostId;
        ChannelId = channelId;
    }

    public string GameId { get; }
    public ulong GuildId { get; }
    public ulong HostId { get; }
    public ulong ChannelId { get; }
    public ulong? PublicMessageId { get; set; }
    public List<TexasPokerPlayer> Players { get; } = [];
    public List<PokerCard> CommunityCards { get; } = [];
    public Queue<PokerCard> Deck { get; } = new();
    public long Pot { get; set; }
    public long CurrentBet { get; set; }
    public int DealerIndex { get; set; }
    public int CurrentPlayerIndex { get; set; } = -1;
    public TexasPokerPhase Phase { get; set; } = TexasPokerPhase.Waiting;
    public string? ResultText { get; set; }
    public List<ulong> WinnerUserIds { get; } = [];
    public Dictionary<ulong, string> HandRankNames { get; } = [];
}
