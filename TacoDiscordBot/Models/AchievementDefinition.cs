namespace TacoDiscordBot.Models;

public sealed class AchievementDefinition
{
    public long Id { get; init; }

    public ulong? RoleId { get; init; }

    public string RoleName { get; init; }

    public string ConditionType { get; init; }

    public long Threshold { get; init; }

    public string Rarity { get; init; }

    public long? ParentAchievementId { get; init; }

    public string ConditionDescription { get; init; }
}
