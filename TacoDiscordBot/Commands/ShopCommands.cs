using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Contexts;
using TacoDiscordBot.Models;

namespace TacoDiscordBot.Commands;

public sealed class ShopCommands : ApplicationCommandModule
{
    private const string Prefix = "shop";

    [SlashCommand("shop", "コインでロールを購入します")]
    public async Task Shop(InteractionContext ctx)
    {
        if (ctx.Guild == null || BotHost.ShopService == null)
        {
            await new InteractionResponseContext(ctx).RespondAsync("このコマンドはDB接続済みのサーバー内で利用できます。", true);
            return;
        }

        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            CreateTopBuilder(ctx.Guild.Id, ctx.User.Id));
    }

    public static async Task HandleComponentInteractionAsync(
        DiscordClient client,
        ComponentInteractionCreateEventArgs e)
    {
        var parts = e.Interaction.Data.CustomId?.Split(':');
        if (parts == null || parts.Length < 4 || parts[0] != Prefix || !ulong.TryParse(parts[2], out var userId))
            return;

        if (e.Interaction.User.Id != userId)
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent("⚠️ このショップ画面を操作できるのは実行者だけです。")
                    .AsEphemeral(true));
            return;
        }

        if (!ulong.TryParse(parts[1], out var guildId) || BotHost.ShopService == null)
            return;

        try
        {
            switch (parts[3])
            {
                case "category":
                    if (Enum.TryParse<ShopRoleCategory>(e.Interaction.Data.Values.FirstOrDefault(), out var category))
                        await UpdateAsync(e, CreateRoleListBuilder(guildId, userId, category));
                    break;
                case "role":
                    if (int.TryParse(e.Interaction.Data.Values.FirstOrDefault(), out var roleIndex))
                        await UpdateAsync(e, await CreateConfirmationBuilderAsync(guildId, userId, roleIndex));
                    break;
                case "back":
                    await UpdateAsync(e, CreateTopBuilder(guildId, userId));
                    break;
                case "cancel":
                    if (parts.Length == 5 && Enum.TryParse<ShopRoleCategory>(parts[4], out var cancelCategory))
                        await UpdateAsync(e, CreateRoleListBuilder(guildId, userId, cancelCategory));
                    break;
                case "purchase":
                    if (parts.Length == 5 && int.TryParse(parts[4], out var purchaseIndex))
                        await PurchaseAsync(e, guildId, userId, purchaseIndex);
                    break;
            }
        }
        catch
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent("❌ ショップの処理中にエラーが発生しました。")
                    .AsEphemeral(true));
        }
    }

    private static DiscordInteractionResponseBuilder CreateTopBuilder(ulong guildId, ulong userId)
    {
        var options = new[]
        {
            new DiscordSelectComponentOption("お手頃ロール", "Affordable", "気軽に購入できるロール"),
            new DiscordSelectComponentOption("ステータスロール", "Status", "ステータスを表すロール"),
            new DiscordSelectComponentOption("高額ロール", "Expensive", "高額なロール")
        };
        var select = new DiscordSelectComponent(
            $"{Prefix}:{guildId}:{userId}:category", "購入したいカテゴリを選択してください", options, false, 1, 1);
        return new DiscordInteractionResponseBuilder()
            .WithContent("🛒 **ロールショップ**\n\n購入したいカテゴリを選択してください。")
            .AddComponents(select);
    }

    private static DiscordInteractionResponseBuilder CreateRoleListBuilder(
        ulong guildId,
        ulong userId,
        ShopRoleCategory category)
    {
        var options = ShopRoleDefinition.All
            .Select((role, index) => new { role, index })
            .Where(item => item.role.Category == category)
            .Select(item => new DiscordSelectComponentOption(
                item.role.DisplayName,
                item.index.ToString(),
                $"{item.role.Price:N0}コイン"))
            .ToArray();
        var select = new DiscordSelectComponent(
            $"{Prefix}:{guildId}:{userId}:role", "購入するロールを選択してください", options, false, 1, 1);
        var back = new DiscordButtonComponent(ButtonStyle.Secondary, $"{Prefix}:{guildId}:{userId}:back", "← 戻る");
        return new DiscordInteractionResponseBuilder()
            .WithContent($"🛒 **{GetCategoryName(category)}**\n\n購入するロールを選択してください。")
            .AddComponents(select)
            .AddComponents(back);
    }

    private static async Task<DiscordInteractionResponseBuilder> CreateConfirmationBuilderAsync(
        ulong guildId,
        ulong userId,
        int roleIndex)
    {
        var definition = ShopRoleDefinition.Find(roleIndex.ToString());
        if (definition == null)
            return new DiscordInteractionResponseBuilder().WithContent("❌ 購入対象のロールが見つかりません。");

        if (BotHost.CoinService == null)
            return new DiscordInteractionResponseBuilder().WithContent("❌ コインサービスを利用できません。");

        var balance = await BotHost.CoinService.GetBalanceAsync(guildId, userId);
        var confirm = new DiscordButtonComponent(ButtonStyle.Success,
            $"{Prefix}:{guildId}:{userId}:purchase:{roleIndex}", "購入");
        var cancel = new DiscordButtonComponent(ButtonStyle.Secondary,
            $"{Prefix}:{guildId}:{userId}:cancel:{definition.Category}", "キャンセル");
        return new DiscordInteractionResponseBuilder()
            .WithContent($"🛒 **ロール購入確認**\n\n{definition.DisplayName}\n\n価格：{definition.Price:N0}コイン\n現在の所持コイン：{balance:N0}コイン\n\nこのロールを購入しますか？")
            .AddComponents(confirm)
            .AddComponents(cancel);
    }

    private static async Task PurchaseAsync(
        ComponentInteractionCreateEventArgs e,
        ulong guildId,
        ulong userId,
        int roleIndex)
    {
        var definition = ShopRoleDefinition.Find(roleIndex.ToString());
        if (definition == null)
            return;

        var result = await BotHost.ShopService.PurchaseAsync(guildId, userId, roleIndex);
        var message = result.Status switch
        {
            ShopPurchaseStatus.Success => $"✅ **購入完了**\n\n{definition.DisplayName}を購入し、付与しました。\n支払い：{definition.Price:N0}コイン\n残りの所持コイン：{result.Balance:N0}コイン",
            ShopPurchaseStatus.InsufficientCoins => $"❌ コインが不足しています。\n\n必要なコイン：{definition.Price:N0}コイン\n現在の所持コイン：{result.Balance:N0}コイン",
            ShopPurchaseStatus.AlreadyPurchased => $"⚠️ {definition.DisplayName}は購入済みです。",
            ShopPurchaseStatus.RoleNotFound => $"❌ Discord上に対象ロール「{definition.RoleName}」が見つかりません。",
            _ => "❌ ロールの購入に失敗しました。コインは変更されていません。"
        };

        await UpdateAsync(e, new DiscordInteractionResponseBuilder().WithContent(message));
    }

    private static async Task UpdateAsync(
        ComponentInteractionCreateEventArgs e,
        DiscordInteractionResponseBuilder builder)
        => await e.Interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage, builder);

    private static string GetCategoryName(ShopRoleCategory category)
        => category switch
        {
            ShopRoleCategory.Affordable => "お手頃ロール",
            ShopRoleCategory.Status => "ステータスロール",
            ShopRoleCategory.Expensive => "高額ロール",
            _ => "ロール"
        };
}
