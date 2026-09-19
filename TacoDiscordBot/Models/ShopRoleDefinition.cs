using System;
using System.Collections.Generic;
using System.Linq;

namespace TacoDiscordBot.Models;

public enum ShopRoleCategory
{
    Affordable,
    Status,
    Expensive
}

public sealed class ShopRoleDefinition
{
    public ShopRoleCategory Category { get; init; }
    public string CategoryName => Category switch
    {
        ShopRoleCategory.Affordable => "お手頃ロール",
        ShopRoleCategory.Status => "ステータスロール",
        ShopRoleCategory.Expensive => "高額ロール",
        _ => throw new ArgumentOutOfRangeException()
    };
    public string Emoji { get; init; }
    public string RoleName { get; init; }
    public string ColorHex { get; init; }
    public long Price { get; init; }
    public string DisplayName => $"{Emoji} {RoleName}";

    public static IReadOnlyList<ShopRoleDefinition> All { get; } =
    [
        Create(ShopRoleCategory.Affordable, "🐣", "ひよっこ", "#95A5A6", 500),
        Create(ShopRoleCategory.Affordable, "💤", "寝不足", "#7F8C8D", 1_000),
        Create(ShopRoleCategory.Affordable, "🧍", "一般人", "#7289DA", 1_000),
        Create(ShopRoleCategory.Affordable, "🍵", "ひとやすみ", "#8BC34A", 1_500),
        Create(ShopRoleCategory.Affordable, "🫠", "ちょっと疲れた", "#78909C", 2_000),
        Create(ShopRoleCategory.Affordable, "👌", "まあまあ", "#5C6BC0", 2_000),
        Create(ShopRoleCategory.Affordable, "🧐", "様子見", "#607D8B", 2_500),
        Create(ShopRoleCategory.Affordable, "🎲", "運試し", "#9B59B6", 3_000),
        Create(ShopRoleCategory.Affordable, "💸", "無駄遣い", "#E67E22", 4_000),
        Create(ShopRoleCategory.Affordable, "😎", "余裕の顔", "#3498DB", 5_000),
        Create(ShopRoleCategory.Affordable, "🥔", "じゃがいも", "#A67C52", 5_000),
        Create(ShopRoleCategory.Affordable, "👀", "見てるだけ", "#95A5A6", 5_000),
        Create(ShopRoleCategory.Affordable, "🧃", "休憩中", "#00A8A8", 5_000),
        Create(ShopRoleCategory.Affordable, "🐢", "のんびり勢", "#2E8B57", 5_000),
        Create(ShopRoleCategory.Affordable, "🤡", "養分候補", "#E91E63", 5_000),
        Create(ShopRoleCategory.Affordable, "🤑", "小銭持ち", "#F1C40F", 5_000),
        Create(ShopRoleCategory.Affordable, "💸", "散財入門", "#D35400", 5_000),
        Create(ShopRoleCategory.Affordable, "🎲", "勝負好き", "#8E44AD", 5_000),
        Create(ShopRoleCategory.Status, "💰", "富裕層", "#2ECC71", 1_000_000),
        Create(ShopRoleCategory.Status, "💎", "資産家", "#3498DB", 3_000_000),
        Create(ShopRoleCategory.Status, "🏦", "大口顧客", "#2980B9", 10_000_000),
        Create(ShopRoleCategory.Status, "💳", "VIP", "#9B59B6", 30_000_000),
        Create(ShopRoleCategory.Status, "⭐", "特別会員", "#F1C40F", 100_000_000),
        Create(ShopRoleCategory.Status, "💠", "プレミアム会員", "#00BFFF", 300_000_000),
        Create(ShopRoleCategory.Status, "👑", "ロイヤル会員", "#8E44AD", 1_000_000_000),
        Create(ShopRoleCategory.Status, "🏛️", "特別顧客", "#34495E", 3_000_000_000),
        Create(ShopRoleCategory.Status, "💎", "最上級顧客", "#00CED1", 10_000_000_000),
        Create(ShopRoleCategory.Status, "👑", "大富豪", "#FFD700", 30_000_000_000),
        Create(ShopRoleCategory.Status, "🏦", "財閥", "#C0392B", 100_000_000_000),
        Create(ShopRoleCategory.Status, "💰", "大資産家", "#27AE60", 200_000_000_000),
        Create(ShopRoleCategory.Status, "💎", "超富裕層", "#00FFFF", 350_000_000_000),
        Create(ShopRoleCategory.Status, "👑", "資産家の頂点", "#FFB700", 500_000_000_000),
        Create(ShopRoleCategory.Status, "🏛️", "超大口顧客", "#7F8C8D", 650_000_000_000),
        Create(ShopRoleCategory.Status, "💠", "プレミアムVIP", "#00E5FF", 750_000_000_000),
        Create(ShopRoleCategory.Status, "👑", "ロイヤルVIP", "#A020F0", 850_000_000_000),
        Create(ShopRoleCategory.Status, "🏆", "特別待遇", "#FFD700", 1_000_000_000_000),
        Create(ShopRoleCategory.Expensive, "💰", "札束で解決", "#27AE60", 10_000_000),
        Create(ShopRoleCategory.Expensive, "🏦", "金で殴るタイプ", "#16A085", 50_000_000),
        Create(ShopRoleCategory.Expensive, "💎", "金銭的余裕", "#3498DB", 100_000_000),
        Create(ShopRoleCategory.Expensive, "👑", "金ならある", "#F1C40F", 500_000_000),
        Create(ShopRoleCategory.Expensive, "💳", "支払いに躊躇なし", "#9B59B6", 1_000_000_000),
        Create(ShopRoleCategory.Expensive, "🤑", "景気のいい人", "#E67E22", 5_000_000_000),
        Create(ShopRoleCategory.Expensive, "🏛️", "スポンサー", "#C0392B", 10_000_000_000),
        Create(ShopRoleCategory.Expensive, "💰", "大口スポンサー", "#E74C3C", 50_000_000_000),
        Create(ShopRoleCategory.Expensive, "💎", "VIPスポンサー", "#00CED1", 100_000_000_000),
        Create(ShopRoleCategory.Expensive, "👑", "筆頭スポンサー", "#FFD700", 1_000_000_000_000)
    ];

    public static ShopRoleDefinition? Find(string roleKey)
        => int.TryParse(roleKey, out var index) && index >= 0 && index < All.Count ? All[index] : null;

    public static IReadOnlyList<ShopRoleDefinition> GetByCategory(ShopRoleCategory category)
        => All.Where(role => role.Category == category).ToArray();

    private static ShopRoleDefinition Create(
        ShopRoleCategory category,
        string emoji,
        string roleName,
        string colorHex,
        long price)
        => new()
        {
            Category = category,
            Emoji = emoji,
            RoleName = roleName,
            ColorHex = colorHex,
            Price = price
        };
}
