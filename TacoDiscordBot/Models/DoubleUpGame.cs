namespace TacoDiscordBot.Models;

public sealed class DoubleUpGame
{
    public DoubleUpGame(ulong guildId, ulong userId, long initialBet)
    {
        GuildId = guildId;
        UserId = userId;
        InitialBet = initialBet;
        CurrentAmount = initialBet;
        State = DoubleUpState.Selecting;
    }

    public ulong GuildId { get; }

    public ulong UserId { get; }

    public long InitialBet { get; }

    public long CurrentAmount { get; set; }

    public int SevenStreak { get; set; }

    public DoubleUpState State { get; set; }

    public DoubleUpChoice? LastChoice { get; set; }

    public int? LastNumber { get; set; }
}

public enum DoubleUpState
{
    Selecting,
    Processing,
    Won,
    Lost,
    CashedOut
}

public enum DoubleUpChoice
{
    High,
    Low
}
