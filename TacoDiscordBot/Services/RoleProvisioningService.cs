using System;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;

namespace TacoDiscordBot.Services;

public sealed class RoleProvisioningService
{
    public async Task<DiscordRole> GetOrCreateAsync(
        DiscordGuild guild,
        string roleName,
        string colorHex,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(guild);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var role = guild.Roles.Values.FirstOrDefault(item => item.Name == roleName);
        if (role != null)
            return role;

        return await guild.CreateRoleAsync(
            roleName,
            color: string.IsNullOrWhiteSpace(colorHex) ? null : new DiscordColor(colorHex),
            reason: reason);
    }
}
