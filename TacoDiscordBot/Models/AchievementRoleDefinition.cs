using System.Collections.Generic;
using System.Linq;

namespace TacoDiscordBot.Models;

public sealed class AchievementRoleDefinition
{
    public string Name { get; init; }
    public string Emoji { get; init; }
    public string ColorHex { get; init; }
    public string DisplayName => $"{Emoji} {Name}";

    public static IReadOnlyList<AchievementRoleDefinition> All { get; } =
    [
        Create("破産者", "💀", "#7F8C8D"),
        Create("破滅者", "☠️", "#566573"),
        Create("人間未満", "🗿", "#34495E"),
        Create("ATM", "🏧", "#5D6D7E"),
        Create("歩く負債", "💸", "#8E44AD"),
        Create("概念", "🌀", "#2C3E50"),
        Create("奇跡の復活者", "🪽", "#00BFFF"),
        Create("死に損ない", "🧟", "#27AE60"),
        Create("大富豪", "💰", "#FFD700"),
        Create("金持ち", "🤑", "#F1C40F"),
        Create("神に愛された者", "🍀", "#2ECC71"),
        Create("ギャンブルの申し子", "🎰", "#E67E22"),
        Create("大貧民", "🥶", "#3498DB"),
        Create("連勝街道", "🔥", "#E74C3C"),
        Create("勝ち馬", "🐎", "#E67E22"),
        Create("全戦全勝", "👑", "#FFD700"),
        Create("負け癖", "😓", "#95A5A6"),
        Create("負け街道", "📉", "#7F8C8D"),
        Create("底なし沼", "🕳️", "#2C3E50"),
        Create("幸運？それとも悪運？", "🤔", "#9B59B6"),
        Create("超奇跡的回避", "🪽", "#00FFFF"),
        Create("確率の超越", "⚜️", "#FFB700")
    ];

    public static AchievementRoleDefinition? Find(string roleName)
    {
        var normalizedName = roleName.Replace(" ", string.Empty);
        return All.FirstOrDefault(definition =>
            definition.Name == roleName
            || definition.DisplayName == roleName
            || definition.DisplayName.Replace(" ", string.Empty) == normalizedName);
    }

    private static AchievementRoleDefinition Create(string name, string emoji, string colorHex)
        => new() { Name = name, Emoji = emoji, ColorHex = colorHex };
}
