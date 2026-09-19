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
    public long Price { get; init; }
    public string DisplayName => $"{Emoji} {RoleName}";

    public static IReadOnlyList<ShopRoleDefinition> All { get; } =
    [
        Create(ShopRoleCategory.Affordable, "🐣", "ひよっこ", 500),
        Create(ShopRoleCategory.Affordable, "💤", "寝不足", 1_000),
        Create(ShopRoleCategory.Affordable, "🧍", "一般人", 1_000),
        Create(ShopRoleCategory.Affordable, "🍵", "ひとやすみ", 1_500),
        Create(ShopRoleCategory.Affordable, "🫠", "ちょっと疲れた", 2_000),
        Create(ShopRoleCategory.Affordable, "👌", "まあまあ", 2_000),
        Create(ShopRoleCategory.Affordable, "🧐", "様子見", 2_500),
        Create(ShopRoleCategory.Affordable, "🎲", "運試し", 3_000),
        Create(ShopRoleCategory.Affordable, "💸", "無駄遣い", 4_000),
        Create(ShopRoleCategory.Affordable, "😎", "余裕の顔", 5_000),
        Create(ShopRoleCategory.Affordable, "🥔", "じゃがいも", 5_000),
        Create(ShopRoleCategory.Affordable, "👀", "見てるだけ", 5_000),
        Create(ShopRoleCategory.Affordable, "🧃", "休憩中", 5_000),
        Create(ShopRoleCategory.Affordable, "🐢", "のんびり勢", 5_000),
        Create(ShopRoleCategory.Affordable, "🤡", "養分候補", 5_000),
        Create(ShopRoleCategory.Affordable, "🤑", "小銭持ち", 5_000),
        Create(ShopRoleCategory.Affordable, "💸", "散財入門", 5_000),
        Create(ShopRoleCategory.Affordable, "🎲", "勝負好き", 5_000),
        Create(ShopRoleCategory.Status, "💰", "富裕層", 1_000_000),
        Create(ShopRoleCategory.Status, "💎", "資産家", 3_000_000),
        Create(ShopRoleCategory.Status, "🏦", "大口顧客", 10_000_000),
        Create(ShopRoleCategory.Status, "💳", "VIP", 30_000_000),
        Create(ShopRoleCategory.Status, "⭐", "特別会員", 100_000_000),
        Create(ShopRoleCategory.Status, "💠", "プレミアム会員", 300_000_000),
        Create(ShopRoleCategory.Status, "👑", "ロイヤル会員", 1_000_000_000),
        Create(ShopRoleCategory.Status, "🏛️", "特別顧客", 3_000_000_000),
        Create(ShopRoleCategory.Status, "💎", "最上級顧客", 10_000_000_000),
        Create(ShopRoleCategory.Status, "👑", "大富豪", 30_000_000_000),
        Create(ShopRoleCategory.Status, "🏦", "財閥", 100_000_000_000),
        Create(ShopRoleCategory.Status, "💰", "大資産家", 200_000_000_000),
        Create(ShopRoleCategory.Status, "💎", "超富裕層", 350_000_000_000),
        Create(ShopRoleCategory.Status, "👑", "資産家の頂点", 500_000_000_000),
        Create(ShopRoleCategory.Status, "🏛️", "超大口顧客", 650_000_000_000),
        Create(ShopRoleCategory.Status, "💠", "プレミアムVIP", 750_000_000_000),
        Create(ShopRoleCategory.Status, "👑", "ロイヤルVIP", 850_000_000_000),
        Create(ShopRoleCategory.Status, "🏆", "特別待遇", 1_000_000_000_000),
        Create(ShopRoleCategory.Expensive, "💰", "札束で解決", 10_000_000),
        Create(ShopRoleCategory.Expensive, "🏦", "金で殴るタイプ", 50_000_000),
        Create(ShopRoleCategory.Expensive, "💎", "金銭的余裕", 100_000_000),
        Create(ShopRoleCategory.Expensive, "👑", "金ならある", 500_000_000),
        Create(ShopRoleCategory.Expensive, "💳", "支払いに躊躇なし", 1_000_000_000),
        Create(ShopRoleCategory.Expensive, "🤑", "景気のいい人", 5_000_000_000),
        Create(ShopRoleCategory.Expensive, "🏛️", "スポンサー", 10_000_000_000),
        Create(ShopRoleCategory.Expensive, "💰", "大口スポンサー", 50_000_000_000),
        Create(ShopRoleCategory.Expensive, "💎", "VIPスポンサー", 100_000_000_000),
        Create(ShopRoleCategory.Expensive, "👑", "筆頭スポンサー", 1_000_000_000_000)
    ];

    public static ShopRoleDefinition? Find(string roleKey)
        => int.TryParse(roleKey, out var index) && index >= 0 && index < All.Count ? All[index] : null;

    public static IReadOnlyList<ShopRoleDefinition> GetByCategory(ShopRoleCategory category)
        => All.Where(role => role.Category == category).ToArray();

    private static ShopRoleDefinition Create(ShopRoleCategory category, string emoji, string roleName, long price)
        => new() { Category = category, Emoji = emoji, RoleName = roleName, Price = price };
}
