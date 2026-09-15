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
    private static readonly (string Symbol, int Weight)[] SymbolWeights =
    [
        ("🍒", 20),
        ("🍋", 18),
        ("🍇", 15),
        ("🍉", 13),
        ("🍈", 12),
        (UltraRare, 9),
        (BigWin, 8),
        (MegaJackpot, 5)
    ];
    private static readonly string[] Symbols = SymbolWeights.Select(item => item.Symbol).ToArray();
    private readonly SlotRepository _repository;
    private readonly ICoinService _coinService;

    public SlotService(SlotRepository repository, ICoinService coinService = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _coinService = coinService;
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
        var symbols = DrawSymbols();
        var rank = DetermineRank(symbols);
        var statistics = await _repository.RecordSpinAsync(rank != SlotWinRank.Loss);
        var payout = bet * GetPayoutMultiplier(rank);
        if (payout > 0)
            await _coinService.AddCoinsAsync(guildId, userId, payout);

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

        return new SlotSpinResult(embed, rank != SlotWinRank.Loss, statistics, bet, payout);
    }

    public static int GetPayoutMultiplier(SlotWinRank rank)
        => rank switch
        {
            SlotWinRank.MegaJackpot => 50,
            SlotWinRank.UltraRare => 10,
            SlotWinRank.BigWin => 15,
            SlotWinRank.Win => 2,
            _ => 0
        };

    // 永続化されているスロット統計を取得します。
    public Task<SlotStatistics> GetStatisticsAsync() => _repository.GetStatisticsAsync();

    // 各リールを独立して抽選し、指定された出現確率を適用します。
    public static string[] DrawSymbols()
    {
        return [DrawSymbol(), DrawSymbol(), DrawSymbol()];
    }

    // 3つの絵柄が揃っているか確認し、当たりランクを決定します。
    public static SlotWinRank DetermineRank(IReadOnlyList<string> symbols)
    {
        if (symbols.Count != 3 || symbols.Any(symbol => symbol == null) || symbols.Distinct().Count() != 1)
            return SlotWinRank.Loss;

        return symbols[0] switch
        {
            MegaJackpot => SlotWinRank.MegaJackpot,
            UltraRare => SlotWinRank.UltraRare,
            BigWin => SlotWinRank.BigWin,
            _ when Symbols.Contains(symbols[0], StringComparer.Ordinal) => SlotWinRank.Win,
            _ => SlotWinRank.Loss
        };
    }

    private static string DrawSymbol()
    {
        // 指定された累積確率の範囲から、1リール分の絵柄を選択します。
        var value = Random.Shared.Next(TotalProbability);
        var boundary = 0;
        foreach (var (symbol, weight) in SymbolWeights)
        {
            boundary += weight;
            if (value < boundary)
                return symbol;
        }

        return SymbolWeights[^1].Symbol;
    }

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
            _ => "🎉 WIN! 🎉"
        };
        var celebration = rank switch
        {
            SlotWinRank.MegaJackpot => "🎊🎊🎊 超・大・当・た・り！！ 🎊🎊🎊\n💰💰💰 JACKPOT！！ 💰💰💰",
            SlotWinRank.UltraRare => "✨✨✨ 激レア大当たり！！ ✨✨✨\n💎 奇跡の3つ揃い！ 💎",
            SlotWinRank.BigWin => "🎉🎉🎉 大当たり！！ 🎉🎉🎉\n🔔 おめでとう！ 🔔",
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
