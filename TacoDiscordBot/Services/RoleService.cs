using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using TacoDiscordBot.Models;
using TacoDiscordBot.Repository;
using TacoDiscordBot.Util;

namespace TacoDiscordBot.Services;

public sealed class RoleService
{
    private readonly DiscordClient _client;
    private readonly AchievementRepository _repository;
    private readonly UserDataRepository _userDataRepository;

    public RoleService(
        DiscordClient client,
        AchievementRepository repository,
        UserDataRepository userDataRepository
    )
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _userDataRepository = userDataRepository ?? throw new ArgumentNullException(nameof(userDataRepository));
    }

    public Task SetNotificationChannelAsync(ulong guildId, ulong channelId)
        => _repository.SetNotificationChannelAsync(guildId, channelId);

    public Task<ulong?> GetRoleNotificationChannelAsync(ulong guildId)
        => _repository.GetNotificationChannelAsync(guildId);

    public async Task InitializeRolesAsync()
    {
        var guildIds = await _repository.GetConfiguredGuildIdsAsync();
        var definitions = await _repository.GetDefinitionsAsync();

        foreach (var guildId in guildIds)
        {
            try
            {
                await InitializeGuildRolesAsync(guildId, definitions);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "RoleService: ギルドの実績ロール初期化に失敗 guild={GuildId}", guildId);
            }
        }
    }

    public Task RefreshUserRolesAsync(ulong guildId, ulong userId)
        => UpdateUserRolesAsync(guildId, userId);

    public async Task RecordEventAsync(
        ulong guildId,
        ulong userId,
        string conditionType,
        long amount = 1,
        bool updateRoles = true
    )
    {
        await _repository.IncrementStatAsync(guildId, userId, conditionType, amount);
        if (updateRoles)
            await UpdateUserRolesAsync(guildId, userId);
    }

    public async Task UpdateUserRolesAsync(ulong guildId, ulong userId)
    {
        var stats = await _repository.GetStatsAsync(guildId, userId);
        var userData = await _userDataRepository.GetOrCreateAsync(guildId, userId);
        var ranking = await _userDataRepository.GetAllAsync(guildId);
        var minimumCoins = ranking.Count == 0 ? userData.Coins : ranking.Min(item => item.Coins);
        var definitions = await _repository.GetDefinitionsAsync();
        var guild = await _client.GetGuildAsync(guildId);
        var member = await guild.GetMemberAsync(userId);

        foreach (var definition in definitions.Where(definition => IsAchieved(definition, stats, userData.Coins, userData.Coins == minimumCoins)))
        {
            await GrantRoleAsync(guildId, member, definition);
        }
    }

    public async Task<bool> GrantRoleAsync(
        ulong guildId,
        DiscordMember member,
        AchievementDefinition definition
    )
    {
        var roleId = await _repository.GetGuildRoleIdAsync(guildId, definition.Id) ?? definition.RoleId;
        if (!roleId.HasValue)
        {
            Logger.Info("RoleService: ロールID未設定のためスキップ achievement={AchievementId}", definition.Id);
            return false;
        }

        if (member.Roles.Any(role => role.Id == roleId.Value))
            return false;

        var role = member.Guild.GetRole(roleId.Value);
        if (role == null)
        {
            Logger.Info("RoleService: ロールが見つからないためスキップ guild={GuildId} role={RoleId}", guildId, roleId);
            return false;
        }

        await member.GrantRoleAsync(role, "実績解除");
        if (!await _repository.TryRecordGrantAsync(guildId, member.Id, definition.Id))
            return false;

        await NotifyRoleGrantedAsync(guildId, member, role, definition);
        return true;
    }

    private async Task InitializeGuildRolesAsync(
        ulong guildId,
        IReadOnlyList<AchievementDefinition> definitions
    )
    {
        var guild = await _client.GetGuildAsync(guildId);
        foreach (var definition in definitions)
        {
            var roleId = await _repository.GetGuildRoleIdAsync(guildId, definition.Id);
            var role = roleId.HasValue ? guild.GetRole(roleId.Value) : null;
            role ??= definition.RoleId.HasValue ? guild.GetRole(definition.RoleId.Value) : null;
            role ??= guild.Roles.Values.FirstOrDefault(item => item.Name == definition.RoleName);
            role ??= await guild.CreateRoleAsync(definition.RoleName, reason: "実績ロール初期化");

            await _repository.SetGuildRoleIdAsync(guildId, definition.Id, role.Id);
            Logger.Info(
                "RoleService: 実績ロールを準備しました guild={GuildId} achievement={AchievementId} role={RoleId}",
                guildId,
                definition.Id,
                role.Id
            );
        }
    }

    private async Task NotifyRoleGrantedAsync(
        ulong guildId,
        DiscordMember member,
        DiscordRole role,
        AchievementDefinition definition
    )
    {
        var channel = await GetRoleNotificationChannelAsync(guildId);
        if (!channel.HasValue)
            return;

        var notificationChannel = await _client.GetChannelAsync(channel.Value);
        var title = definition.Rarity.Equals("rare", StringComparison.OrdinalIgnoreCase)
            ? "🎉 超レア実績解除！"
            : definition.ParentAchievementId.HasValue
                ? "🏆 実績ランクアップ！"
                : "🏆 実績解除！";
        var body = definition.ParentAchievementId.HasValue
            ? $"<@{member.Id}> が上位ロールを獲得しました！\n\n{role.Mention} {definition.RoleName}"
            : $"<@{member.Id}> がロールを獲得しました！\n\nロール:\n{role.Mention} {definition.RoleName}";

        await notificationChannel.SendMessageAsync(
            $"{title}\n\n{body}\n\n条件:\n{definition.ConditionDescription}"
        );
    }

    private static bool IsAchieved(
        AchievementDefinition definition,
        AchievementStats stats,
        long coins,
        bool isLowestBalance
    )
        => definition.ConditionType switch
        {
            "bankruptcy_count" => stats.BankruptcyCount >= definition.Threshold,
            "rare_slot_count" => stats.RareSlotCount >= definition.Threshold,
            "mines_safe_count" => stats.MaxMinesSafeCount >= definition.Threshold,
            "lastchance_jackpot_count" => stats.LastChanceJackpotCount >= definition.Threshold,
            "lastchance_zero_count" => stats.LastChanceZeroCount >= definition.Threshold,
            "coins" => coins >= definition.Threshold,
            "god_achievement" => stats.RareSlotCount >= definition.Threshold
                || stats.MaxMinesSafeCount >= 15,
            "lowest_balance" => isLowestBalance,
            "blackjack_win_streak" => stats.BlackjackWinStreak >= definition.Threshold,
            "blackjack_loss_streak" => stats.BlackjackLossStreak >= definition.Threshold,
            _ => false
        };
}
