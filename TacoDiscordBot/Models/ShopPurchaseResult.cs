namespace TacoDiscordBot.Models;

public enum ShopPurchaseStatus
{
    Success,
    InsufficientCoins,
    AlreadyPurchased,
    RoleNotFound,
    Failed
}

public sealed class ShopPurchaseResult
{
    public ShopPurchaseStatus Status { get; init; }
    public long Balance { get; init; }
    public string? ErrorMessage { get; init; }
}
