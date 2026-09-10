using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using TacoDiscordBot.Models;
using TacoDiscordBot.Repository;

namespace TacoDiscordBot.Services;

public sealed class SlotService
{
    private const string MegaJackpot = "7️⃣";
    private const string UltraRare = "💎";
    private const string BigWin = "🔔";
    private const double MegaJackpotProbability = 1d / 300d;
    private const double UltraRareProbability = 1d / 200d;
    private const double BigWinProbability = 1d / 100d;
    private static readonly string[] RegularSymbols = ["🍒", "🍋", "🍇", "🍉", "🍈"];
    private static readonly string[] Symbols = ["🍒", "🍋", "🍇", "🍉", "🍈", "💎", "🔔", "7️⃣"];
    private readonly SlotRepository _repository;

    public SlotService(SlotRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    // 1回分の抽選、当たり判定、統計更新、結果Embedの作成をまとめて実行します。
    public async Task<SlotSpinResult> SpinAsync()
    {
        var symbols = DrawSymbols();
        var rank = DetermineRank(symbols);
        var statistics = await _repository.RecordSpinAsync(rank != SlotWinRank.Loss);
        var embed = CreateEmbed(symbols, rank, statistics);

        return new SlotSpinResult(embed, rank != SlotWinRank.Loss, statistics);
    }

    // 永続化されているスロット統計を取得します。
    public Task<SlotStatistics> GetStatisticsAsync() => _repository.GetStatisticsAsync();

    // 各リールを独立して抽選し、特別絵柄の3つ揃い確率を満たす重みを適用します。
    public static string[] DrawSymbols()
    {
        var regularProbability =
            (1d - MegaJackpotProbabilityRoot - UltraRareProbabilityRoot - BigWinProbabilityRoot) /
            RegularSymbols.Length;

        return [DrawSymbol(regularProbability), DrawSymbol(regularProbability), DrawSymbol(regularProbability)];
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

    private static double MegaJackpotProbabilityRoot => Math.Pow(MegaJackpotProbability, 1d / 3d);
    private static double UltraRareProbabilityRoot => Math.Pow(UltraRareProbability, 1d / 3d);
    private static double BigWinProbabilityRoot => Math.Pow(BigWinProbability, 1d / 3d);

    private static string DrawSymbol(double regularProbability)
    {
        // 指定された累積確率の範囲から、1リール分の絵柄を選択します。
        var value = Random.Shared.NextDouble();
        var boundary = regularProbability;

        foreach (var symbol in RegularSymbols)
        {
            if (value < boundary)
                return symbol;

            boundary += regularProbability;
        }

        if (value < boundary + UltraRareProbabilityRoot)
            return UltraRare;

        boundary += UltraRareProbabilityRoot;
        if (value < boundary + BigWinProbabilityRoot)
            return BigWin;

        return MegaJackpot;
    }

    private static DiscordEmbed CreateEmbed(
        IReadOnlyList<string> symbols,
        SlotWinRank rank,
        SlotStatistics statistics
    )
    {
        // 当たり時はランク別の色・演出とスロット統計をEmbedへまとめます。
        var result = string.Join(" ", symbols);
        if (rank == SlotWinRank.Loss)
        {
            return new DiscordEmbedBuilder()
                .WithTitle("🎰 スロット")
                .WithDescription($"{result}\nハズレ")
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
            SlotWinRank.MegaJackpot => "💰 超当たり！！ 💰",
            SlotWinRank.UltraRare => "✨ 激レア！！ ✨",
            SlotWinRank.BigWin => "🎉 大当たり！！ 🎉",
            _ => "🎊 3つ揃った！！ 🎊"
        };
        var embed = new DiscordEmbedBuilder()
            .WithTitle(title)
            .WithDescription($"**{result}**\n\n{celebration}")
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

public sealed record SlotSpinResult(DiscordEmbed Embed, bool IsHit, SlotStatistics Statistics);
