using System;
using System.Threading.Tasks;
using DSharpPlus;
using TacoDiscordBot.Models;
using TacoDiscordBot.Repository;
using TacoDiscordBot.Services.Interface;

namespace TacoDiscordBot.Services;

public sealed class ShopService
{
    private readonly DiscordClient _client;
    private readonly ICoinService _coinService;
    private readonly UserDataRepository _repository;
    private readonly RoleProvisioningService _roleProvisioningService;

    public ShopService(
        DiscordClient client,
        ICoinService coinService,
        UserDataRepository repository,
        RoleProvisioningService? roleProvisioningService = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _coinService = coinService ?? throw new ArgumentNullException(nameof(coinService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _roleProvisioningService = roleProvisioningService ?? new RoleProvisioningService();
    }

    public async Task<ShopPurchaseResult> PurchaseAsync(ulong guildId, ulong userId, int roleIndex)
    {
        var definition = roleIndex >= 0 && roleIndex < ShopRoleDefinition.All.Count
            ? ShopRoleDefinition.All[roleIndex]
            : null;
        if (definition == null)
            return new ShopPurchaseResult { Status = ShopPurchaseStatus.Failed, ErrorMessage = "購入対象のロールが見つかりません。" };

        var roleKey = roleIndex.ToString();
        if (await _repository.HasPurchasedShopRoleAsync(guildId, userId, roleKey))
        {
            var balance = await _coinService.GetBalanceAsync(guildId, userId);
            return new ShopPurchaseResult { Status = ShopPurchaseStatus.AlreadyPurchased, Balance = balance };
        }

        var guild = await _client.GetGuildAsync(guildId);
        var role = await _roleProvisioningService.GetOrCreateAsync(
            guild,
            definition.RoleName,
            definition.ColorHex,
            "ショップ購入ロールの作成");

        var member = await guild.GetMemberAsync(userId);
        if (member.Roles.Any(item => item.Id == role.Id))
        {
            await _repository.RecordShopRolePurchaseAsync(guildId, userId, roleKey);
            var balance = await _coinService.GetBalanceAsync(guildId, userId);
            return new ShopPurchaseResult { Status = ShopPurchaseStatus.AlreadyPurchased, Balance = balance };
        }

        var currentBalance = await _coinService.GetBalanceAsync(guildId, userId);
        if (currentBalance < definition.Price)
            return new ShopPurchaseResult { Status = ShopPurchaseStatus.InsufficientCoins, Balance = currentBalance };

        long remainingBalance;
        try
        {
            remainingBalance = await _coinService.RemoveCoinsAsync(guildId, userId, definition.Price);
        }
        catch (InvalidOperationException)
        {
            remainingBalance = await _coinService.GetBalanceAsync(guildId, userId);
            return new ShopPurchaseResult { Status = ShopPurchaseStatus.InsufficientCoins, Balance = remainingBalance };
        }

        try
        {
            await member.GrantRoleAsync(role, "ショップでのロール購入");
            var recorded = await _repository.RecordShopRolePurchaseAsync(guildId, userId, roleKey);
            if (!recorded)
            {
                await _coinService.AddCoinsAsync(guildId, userId, definition.Price);
                return new ShopPurchaseResult { Status = ShopPurchaseStatus.AlreadyPurchased, Balance = remainingBalance + definition.Price };
            }

            return new ShopPurchaseResult { Status = ShopPurchaseStatus.Success, Balance = remainingBalance };
        }
        catch (Exception ex)
        {
            await _coinService.AddCoinsAsync(guildId, userId, definition.Price);
            return new ShopPurchaseResult
            {
                Status = ShopPurchaseStatus.Failed,
                Balance = await _coinService.GetBalanceAsync(guildId, userId),
                ErrorMessage = ex.Message
            };
        }
    }
}
