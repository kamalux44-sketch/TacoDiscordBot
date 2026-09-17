using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using TacoDiscordBot.Models;
using TacoDiscordBot.Repository;
using TacoDiscordBot.Services.Interface;

namespace TacoDiscordBot.Services;

public sealed class SlotService
{
    private const string MegaJackpot = "7️⃣";
    private const string UltraRare = "💎";
    private const string BigWin = "🔔";
    private const int TotalProbability = 100;
    private static readonly SlotSymbolConfiguration[] SymbolConfigurations =
    [
        new("🍒", 0.24m, 8m, 0m),
        new("🍋", 0.22m, 12m, 0m),
        new("🍇", 0.15m, 20m, 0.5m),
        new("🍉", 0.11m, 35m, 1m),
        new("🍈", 0.09m, 55m, 1.5m),
        new(BigWin, 0.09m, 160m, 4m),
        new(UltraRare, 0.06m, 500m, 6m),
        new(MegaJackpot, 0.04m, 1000m, 10m)
    ];
    private static readonly string[] Symbols = SymbolConfigurations.Select(item => item.Symbol).ToArray();
    private readonly SlotRepository _repository;
    private readonly ICoinService _coinService;
    private readonly RoleService _roleService;
    private readonly EventManager _eventManager;

    public SlotService(
        SlotRepository repository,
        ICoinService coinService = null,
        RoleService? roleService = null,
        EventManager? eventManager = null
    )
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _coinService = coinService;
        _roleService = roleService;
        _eventManager = eventManager;
    }

    // 1回分の抽選、当たり判定、統計更新、結果Embedの作成をまとめて実行します。
    public async Task<SlotSpinResult> SpinAsync(
        ulong guildId,
        ulong userId,
        long bet,
        Func<IReadOnlyList<string>, Task>? onReelRevealed = null
    )
    {
        if (_coinService == null)
            throw new InvalidOperationException("コインサービスは未設定です。");

        if (bet <= 0)
            throw new ArgumentOutOfRangeException(nameof(bet), "ベットは1以上で指定してください。");

        await _coinService.RemoveCoinsAsync(guildId, userId, bet);
        var symbols = DrawSymbols(_eventManager?.GetActiveEvent(guildId)?.Type == EventType.HotSlot);
        var rank = DetermineRank(symbols);
        var payout = CalculatePayout(bet, symbols, rank);
        // 払い戻しが発生しないリーチは、敗北として扱います。
        if (rank == SlotWinRank.Reach && payout == 0)
            rank = SlotWinRank.Loss;
        if (_eventManager != null)
        {
            payout = _eventManager.CalculatePayout(guildId, userId, payout, "slot");
            if (payout == 0 && rank == SlotWinRank.Loss)
                payout = _eventManager.CalculateLossRefund(guildId, userId, bet);
            // Slotは個人イベントの予定払い戻し額計算対象外ですが、1プレイで消費します。
            _eventManager.ConsumePersonalEvent(guildId, userId);
        }
        // リーチは払い戻しがあっても、統計上の当たりには含めません。
        var isHit = rank is SlotWinRank.Win or SlotWinRank.BigWin or SlotWinRank.UltraRare or SlotWinRank.MegaJackpot;
        var statistics = await _repository.RecordSpinAsync(isHit);
        if (payout > 0)
            await _coinService.AddCoinsAsync(guildId, userId, payout);

        if (_roleService != null && rank is SlotWinRank.MegaJackpot or SlotWinRank.UltraRare or SlotWinRank.BigWin)
            await _roleService.RecordEventAsync(guildId, userId, "rare_slot_count");
        else if (_roleService != null)
            await _roleService.RefreshUserRolesAsync(guildId, userId);

        // 各リールの確定結果を順番に通知し、呼び出し側で表示を更新します。
        if (onReelRevealed != null)
        {
            var revealedSymbols = new List<string>(symbols.Length);
            foreach (var symbol in symbols)
            {
                revealedSymbols.Add(symbol);
                await onReelRevealed(revealedSymbols.ToArray());
            }
        }

        var embed = CreateEmbed(symbols, rank, statistics, bet, payout);

        return new SlotSpinResult(embed, isHit, statistics, bet, payout);
    }

    public static decimal GetPayoutMultiplier(SlotWinRank rank)
        => rank switch
        {
            SlotWinRank.MegaJackpot => GetConfiguration(MegaJackpot).ThreeMatchMultiplier,
            SlotWinRank.UltraRare => GetConfiguration(UltraRare).ThreeMatchMultiplier,
            SlotWinRank.BigWin => GetConfiguration(BigWin).ThreeMatchMultiplier,
            SlotWinRank.Win => GetConfiguration("🍒").ThreeMatchMultiplier,
            _ => 0m
        };

    public static long CalculatePayout(long bet, IReadOnlyList<string> symbols, SlotWinRank rank)
    {
        if (rank == SlotWinRank.Loss)
            return 0;

        var symbol = rank == SlotWinRank.MegaJackpot || rank == SlotWinRank.UltraRare
            || rank == SlotWinRank.BigWin || rank == SlotWinRank.Win
            ? symbols[0]
            : symbols.First(symbol => symbols.Count(value => value == symbol) == 2);
        var multiplier = rank == SlotWinRank.Reach
            ? GetConfiguration(symbol).ReachMultiplier
            : GetConfiguration(symbol).ThreeMatchMultiplier;
        return checked((long)Math.Floor(bet * multiplier));
    }

    // 永続化されているスロット統計を取得します。
    public Task<SlotStatistics> GetStatisticsAsync() => _repository.GetStatisticsAsync();

    // 各リールを独立して抽選し、指定された出現確率を適用します。
    public static string[] DrawSymbols()
    {
        return DrawSymbols(false);
    }

    private static string[] DrawSymbols(bool hotSlot)
    {
        var configurations = hotSlot ? HotSlotConfigurations : SymbolConfigurations;
        return [DrawSymbol(configurations), DrawSymbol(configurations), DrawSymbol(configurations)];
    }

    private static readonly SlotSymbolConfiguration[] HotSlotConfigurations =
    [
        new("🍒", 0.16m, 8m, 0m), new("🍋", 0.14m, 12m, 0m),
        new("🍇", 0.10m, 20m, 0.5m), new("🍉", 0.08m, 35m, 1m),
        new("🍈", 0.10m, 55m, 1.5m), new(UltraRare, 0.14m, 160m, 4m),
        new(BigWin, 0.19m, 500m, 6m), new(MegaJackpot, 0.09m, 1000m, 10m)
    ];

    // 3つの絵柄が揃っているか確認し、当たりランクを決定します。
    public static SlotWinRank DetermineRank(IReadOnlyList<string> symbols)
    {
        if (symbols.Count != 3 || symbols.Any(symbol => symbol == null) || symbols.Any(symbol => !Symbols.Contains(symbol)))
            return SlotWinRank.Loss;

        if (symbols.Distinct().Count() == 1)
            return symbols[0] switch
            {
                MegaJackpot => SlotWinRank.MegaJackpot,
                UltraRare => SlotWinRank.UltraRare,
                BigWin => SlotWinRank.BigWin,
                _ => SlotWinRank.Win
            };

        return symbols.GroupBy(symbol => symbol).Any(group => group.Count() == 2)
            ? SlotWinRank.Reach
            : SlotWinRank.Loss;
    }

    private static string DrawSymbol(IReadOnlyList<SlotSymbolConfiguration> configurations)
    {
        // 指定された累積確率の範囲から、1リール分の絵柄を選択します。
        var value = Random.Shared.Next(TotalProbability);
        var boundary = 0;
        foreach (var configuration in configurations)
        {
            boundary += (int)(configuration.Probability * TotalProbability);
            if (value < boundary)
                return configuration.Symbol;
        }

        return configurations[^1].Symbol;
    }

    private static SlotSymbolConfiguration GetConfiguration(string symbol)
        => SymbolConfigurations.First(configuration => configuration.Symbol == symbol);

    private static DiscordEmbed CreateEmbed(
        IReadOnlyList<string> symbols,
        SlotWinRank rank,
        SlotStatistics statistics,
        long bet,
        long payout
    )
    {
        // 当たり時はランク別の色・演出とスロット統計をEmbedへまとめます。
        var result = string.Join(" ", symbols);
        if (rank == SlotWinRank.Loss)
        {
            return new DiscordEmbedBuilder()
                .WithTitle("🎰 スロット")
                .WithDescription($"{result}\n\n😢 残念！もう一度挑戦してみよう！\nベット: {bet:N0} / 払い戻し: 0")
                .WithColor(DiscordColor.Blurple)
                .Build();
        }

        var title = rank switch
        {
            SlotWinRank.MegaJackpot => "🎰 MEGA JACKPOT 🎰",
            SlotWinRank.UltraRare => "💎 ULTRA RARE 💎",
            SlotWinRank.BigWin => "🔔 BIG WIN 🔔",
            SlotWinRank.Reach => "🔥 リーチ！ 🔥",
            _ => "🎉 WIN! 🎉"
        };
        var celebration = rank switch
        {
            SlotWinRank.MegaJackpot => "🎊🎊🎊 超・大・当・た・り！！ 🎊🎊🎊\n💰💰💰 JACKPOT！！ 💰💰💰",
            SlotWinRank.UltraRare => "✨✨✨ 激レア大当たり！！ ✨✨✨\n💎 奇跡の3つ揃い！ 💎",
            SlotWinRank.BigWin => "🎉🎉🎉 大当たり！！ 🎉🎉🎉\n🔔 おめでとう！ 🔔",
            SlotWinRank.Reach => "🔥 リーチ！\n💰 コイン獲得！",
            _ => "🎊🎊🎊 3つ揃った！！ 🎊🎊🎊"
        };
        var embed = new DiscordEmbedBuilder()
            .WithTitle(title)
            .WithDescription($"**{result}**\n\n{celebration}\nベット: {bet:N0} / 払い戻し: {payout:N0}")
            .WithColor(rank switch
            {
                SlotWinRank.MegaJackpot => DiscordColor.Yellow,
                SlotWinRank.UltraRare => DiscordColor.Blurple,
                SlotWinRank.BigWin => DiscordColor.Green,
                SlotWinRank.Reach => DiscordColor.Orange,
                _ => DiscordColor.Green
            })
            .AddField("最長ハマり", $"{statistics.LongestHitInterval}回転", true)
            .AddField("最短当たり", $"{statistics.ShortestHitInterval?.ToString() ?? "未記録"}回転", true)
            .AddField("累計回転数", $"{statistics.TotalSpins}回", true);

        if (statistics.LastHitInterval.HasValue)
            embed.AddField("前回のあたりから", $"{statistics.LastHitInterval.Value}回転ぶり！", false);

        return embed.Build();
    }
}

public enum SlotWinRank
{
    Loss,
    Reach,
    Win,
    BigWin,
    UltraRare,
    MegaJackpot
}

public sealed record SlotSpinResult(
    DiscordEmbed Embed,
    bool IsHit,
    SlotStatistics Statistics,
    long Bet,
    long Payout
);
