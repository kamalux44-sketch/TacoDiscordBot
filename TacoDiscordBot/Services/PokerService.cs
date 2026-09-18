using System.Collections.Concurrent;
using TacoDiscordBot.Models;
using TacoDiscordBot.Repository;

namespace TacoDiscordBot.Services;

public sealed class PokerService
{
    public const int MinPlayers = 2;
    public const int MaxPlayers = 6;
    public const long InitialChips = 1000;
    public const long MinimumBet = 100;
    public const long MinimumRaise = 100;
    private readonly CoinService _coinService;
    private readonly PokerRepository? _repository;
    private readonly ConcurrentDictionary<string, PokerGame> _games = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random = new();

    public PokerService(CoinService coinService, PokerRepository? repository = null)
    {
        _coinService = coinService ?? throw new ArgumentNullException(nameof(coinService));
        _repository = repository;
    }

    public async Task CloseDueToMissingPublicMessageAsync(string tableId)
    {
        var game = GetGame(tableId);
        await EnterLockAsync(game);
        try
        {
            if (game.Settled)
                return;

            var participants = game.Players.ToList();
            if (participants.Count > 0 && game.Pot > 0)
            {
                var share = game.Pot / participants.Count;
                var remainder = game.Pot % participants.Count;
                foreach (var player in participants)
                    player.Chips += share;
                for (var index = 0; index < remainder; index++)
                    participants[index].Chips++;
            }

            game.Pot = 0;
            game.Phase = PokerPhase.Finished;
            game.WinnerText = "公開卓メッセージ削除により卓終了";
            game.PublicMessageId = null;
            await PersistAsync(game);

            foreach (var player in participants)
            {
                var payout = CalculateCoinPayout(player.Chips, game.CoinRate);
                if (payout > 0)
                    await _coinService.AddCoinsAsync(game.GuildId, player.UserId, payout);
                player.Chips = 0;
            }

            game.Settled = true;
            await PersistAsync(game);
        }
        finally
        {
            gameLock.Release();
        }
    }

    public async Task<PokerGame> CreateGameAsync(ulong guildId, ulong creatorId, long coinRate, ulong channelId)
    {
        if (coinRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(coinRate), "参加費は1 Coin以上で指定してください。");

        string tableId;
        do
        {
            tableId = CreateTableId();
        } while (_games.ContainsKey(tableId));

        var game = new PokerGame(tableId, guildId, creatorId, coinRate, channelId);
        if (!_games.TryAdd(tableId, game))
            throw new InvalidOperationException("卓を作成できませんでした。");
        await PersistAsync(game);
        return game;
    }

    public async Task RestoreAsync()
    {
        if (_repository == null)
            return;

        foreach (var game in await _repository.LoadUnsettledAsync())
        {
            if (!_games.TryAdd(game.TableId, game))
                continue;

            if (game.Phase == PokerPhase.Finished)
                await SettleAsync(game.TableId);
        }
    }

    public PokerGame? Find(string tableId)
        => _games.TryGetValue(tableId, out var game) ? game : null;

    public IReadOnlyList<PokerGame> GetGames()
        => _games.Values.ToArray();

    public async Task SetPublicMessageIdAsync(string tableId, ulong? messageId)
    {
        var game = GetGame(tableId);
        await EnterLockAsync(game);
        try
        {
            game.PublicMessageId = messageId;
            await PersistAsync(game);
        }
        finally
        {
            gameLock.Release();
        }
    }

    public async Task<PokerJoinResult> JoinAsync(string tableId, ulong userId, string displayName)
    {
        var game = GetGame(tableId);
        await EnterLockAsync(game);
        try
        {
            if (game.Phase != PokerPhase.Waiting)
                throw new InvalidOperationException("ゲーム開始後は参加できません。");
            if (game.Players.Any(player => player.UserId == userId))
                throw new InvalidOperationException("既に参加しています。");
            if (game.Players.Count >= MaxPlayers)
                throw new InvalidOperationException("この卓は満員です。");

            await _coinService.RemoveCoinsAsync(game.GuildId, userId, game.CoinRate);
            game.Players.Add(new PokerPlayer(userId, displayName, InitialChips));
            await PersistAsync(game);
            return new PokerJoinResult(game, false);
        }
        finally
        {
            gameLock.Release();
        }
    }

    public async Task<PokerGame> StartAsync(string tableId, ulong userId)
    {
        var game = GetGame(tableId);
        await EnterLockAsync(game);
        try
        {
            if (game.Phase != PokerPhase.Waiting)
                throw new InvalidOperationException("この卓はすでに開始されています。");
            if (game.CreatorId != userId)
                throw new InvalidOperationException("卓の作成者だけがゲームを開始できます。");
            if (game.Players.Count < MinPlayers)
                throw new InvalidOperationException("ゲーム開始には2人以上の参加者が必要です。");

            StartGame(game);
            await PersistAsync(game);
            return game;
        }
        finally
        {
            gameLock.Release();
        }
    }

    public PokerGameSnapshot GetSnapshot(string tableId)
    {
        var game = GetGame(tableId);
        return CreateSnapshot(game);
    }

    public PokerPrivateSnapshot GetPrivateSnapshot(string tableId, ulong userId)
    {
        var game = GetGame(tableId);
        var player = GetPlayer(game, userId);
        return new PokerPrivateSnapshot(game, player);
    }

    public async Task<PokerActionResult> ActAsync(string tableId, ulong userId, PokerAction action, long amount = 0)
    {
        var game = GetGame(tableId);
        await EnterLockAsync(game);
        try
        {
            var player = GetCurrentPlayer(game, userId);
            if (game.Phase is not (PokerPhase.BetRound1 or PokerPhase.BetRound2))
                throw new InvalidOperationException("現在はBetフェーズではありません。");
            if (player.Folded || player.AllIn)
                throw new InvalidOperationException("このプレイヤーは操作できません。");

            var added = action switch
            {
                PokerAction.Check => Check(game, player),
                PokerAction.Bet => Bet(game, player, amount),
                PokerAction.Call => Call(game, player),
                PokerAction.Raise => Raise(game, player, amount),
                PokerAction.Fold => Fold(game, player),
                PokerAction.AllIn => AllIn(game, player),
                _ => throw new InvalidOperationException("無効な操作です。")
            };

            game.Pot += added;
            AdvanceAfterAction(game, player, action, added);
            await PersistAsync(game);
            return new PokerActionResult(game, IsFinished(game));
        }
        finally
        {
            gameLock.Release();
        }
    }

    public async Task<PokerExchangeResult> ExchangeAsync(string tableId, ulong userId, IReadOnlyCollection<int> indexes)
    {
        var game = GetGame(tableId);
        await EnterLockAsync(game);
        try
        {
            if (game.Phase != PokerPhase.Exchange)
                throw new InvalidOperationException("現在はカード交換フェーズではありません。");
            var player = GetPlayer(game, userId);
            if (player.Folded || player.Exchanged)
                throw new InvalidOperationException("このプレイヤーはカード交換済みです。");
            if (indexes.Any(index => index < 0 || index >= player.Hand.Count) || indexes.Count != indexes.Distinct().Count())
                throw new ArgumentException("交換するカードの指定が不正です。", nameof(indexes));

            var orderedIndexes = indexes.OrderByDescending(index => index).ToArray();
            foreach (var index in orderedIndexes)
                player.Hand[index] = game.Deck.Dequeue();
            player.Exchanged = true;
            game.ActionHistory.Add($"{player.DisplayName}：{indexes.Count}枚交換");

            if (game.Players.Where(active => !active.Folded).All(active => active.Exchanged))
            {
                game.Phase = PokerPhase.BetRound2;
                game.BetRound = 2;
                game.CurrentBet = 0;
                game.CurrentPlayerIndex = FindNextActivePlayer(game, -1);
                foreach (var active in game.Players)
                    active.CurrentBet = 0;
            }

            await PersistAsync(game);
            return new PokerExchangeResult(game, indexes.Count);
        }
        finally
        {
            gameLock.Release();
        }
    }

    public static long CalculateCoinPayout(long chips, long coinRate)
    {
        if (chips < 0 || coinRate <= 0)
            throw new ArgumentOutOfRangeException();
        return chips * coinRate / InitialChips;
    }

    public async Task<IReadOnlyDictionary<ulong, long>> SettleAsync(string tableId)
    {
        var game = GetGame(tableId);
        await EnterLockAsync(game);
        try
        {
            if (game.Phase != PokerPhase.Finished)
                throw new InvalidOperationException("ゲーム終了前は払い戻しできません。");
            if (game.Settled)
                return game.Players.ToDictionary(player => player.UserId, _ => 0L);

            var payouts = new Dictionary<ulong, long>();
            foreach (var player in game.Players)
            {
                var payout = CalculateCoinPayout(player.Chips, game.CoinRate);
                if (payout > 0)
                    await _coinService.AddCoinsAsync(game.GuildId, player.UserId, payout);
                payouts[player.UserId] = payout;
                player.Chips = 0;
            }
            game.Settled = true;
            await PersistAsync(game);
            return payouts;
        }
        finally
        {
            gameLock.Release();
        }
    }

    private readonly SemaphoreSlim gameLock = new(1, 1);
    private Task EnterLockAsync(PokerGame game) => gameLock.WaitAsync();

    private void StartGame(PokerGame game)
    {
        game.Phase = PokerPhase.BetRound1;
        game.Deck = new Queue<PokerCard>(CreateDeck().OrderBy(_ => _random.Next()));
        foreach (var player in game.Players)
            for (var card = 0; card < 5; card++)
                player.Hand.Add(game.Deck.Dequeue());
        game.CurrentPlayerIndex = 0;
    }

    private static long Check(PokerGame game, PokerPlayer player)
    {
        if (game.CurrentBet != 0)
            throw new InvalidOperationException("Betが存在するためCheckできません。");
        return 0;
    }

    private static long Bet(PokerGame game, PokerPlayer player, long target)
    {
        ValidateBetAmount(target, MinimumBet);
        if (game.CurrentBet != 0 || target > player.Chips)
            throw new InvalidOperationException("Bet額が不正です。");
        return AddToTarget(player, target);
    }

    private static long Call(PokerGame game, PokerPlayer player)
    {
        var required = game.CurrentBet - player.CurrentBet;
        if (required <= 0 || required > player.Chips)
            throw new InvalidOperationException("CallできるBetがありません。");
        return AddToTarget(player, game.CurrentBet);
    }

    private static long Raise(PokerGame game, PokerPlayer player, long target)
    {
        ValidateBetAmount(target, game.CurrentBet + MinimumRaise);
        if (target > player.Chips + player.CurrentBet)
            throw new InvalidOperationException("Raise額が所持Chipを超えています。");
        return AddToTarget(player, target);
    }

    private static long Fold(PokerGame game, PokerPlayer player)
    {
        player.Folded = true;
        return 0;
    }

    private static long AllIn(PokerGame game, PokerPlayer player)
    {
        var target = player.CurrentBet + player.Chips;
        if (target < game.CurrentBet && game.CurrentBet - target > player.Chips)
            throw new InvalidOperationException("Side PotなしではこのAll-inを処理できません。");
        var added = AddToTarget(player, target);
        player.AllIn = true;
        return added;
    }

    private static long AddToTarget(PokerPlayer player, long target)
    {
        var added = target - player.CurrentBet;
        if (added <= 0 || added > player.Chips)
            throw new InvalidOperationException("追加するChipが不正です。");
        player.Chips -= added;
        player.CurrentBet = target;
        return added;
    }

    private static void ValidateBetAmount(long amount, long minimum)
    {
        if (amount < minimum || amount % 10 != 0)
            throw new InvalidOperationException("Betは10 Chip単位で、最低条件を満たす必要があります。");
    }

    private static void AdvanceAfterAction(PokerGame game, PokerPlayer player, PokerAction action, long added)
    {
        player.ActionHistory.Add($"{action.ToDisplayString(added)}");
        game.ActionHistory.Add($"{player.DisplayName}：{player.ActionHistory[^1]}");
        if (game.Players.Count(active => !active.Folded) == 1)
        {
            FinishGame(game);
            return;
        }

        var activePlayers = game.Players.Where(active => !active.Folded).ToList();
        if (activePlayers.All(active => active.AllIn || active.CurrentBet == game.CurrentBet))
        {
            if (game.Phase == PokerPhase.BetRound1)
            {
                game.Phase = PokerPhase.Exchange;
                game.CurrentPlayerIndex = -1;
                return;
            }
            FinishGame(game);
            return;
        }

        game.CurrentPlayerIndex = FindNextActivePlayer(game, game.CurrentPlayerIndex);
    }

    private static void FinishGame(PokerGame game)
    {
        var contenders = game.Players.Where(player => !player.Folded).ToList();
        var evaluations = contenders.ToDictionary(player => player, player => PokerHandEvaluator.Evaluate(player.Hand));
        var best = evaluations.Values.Max()!;
        var winners = evaluations.Where(pair => pair.Value.CompareTo(best) == 0).Select(pair => pair.Key).ToList();
        var share = game.Pot / winners.Count;
        var remainder = game.Pot % winners.Count;
        foreach (var winner in winners)
            winner.Chips += share;
        for (var index = 0; index < remainder; index++)
            winners[index].Chips++;
        game.WinnerText = string.Join("、", winners.Select(winner => $"{winner.DisplayName}（{evaluations[winner].CategoryDisplayName}）"));
        game.Pot = 0;
        game.Phase = PokerPhase.Finished;
    }

    private static int FindNextActivePlayer(PokerGame game, int currentIndex)
    {
        for (var offset = 1; offset <= game.Players.Count; offset++)
        {
            var index = (currentIndex + offset) % game.Players.Count;
            var player = game.Players[index];
            if (!player.Folded && !player.AllIn)
                return index;
        }
        return -1;
    }

    private PokerGame GetGame(string tableId)
        => Find(tableId) ?? throw new InvalidOperationException("指定された卓は存在しません。");

    private static PokerPlayer GetPlayer(PokerGame game, ulong userId)
        => game.Players.FirstOrDefault(player => player.UserId == userId)
            ?? throw new InvalidOperationException("この卓の参加者ではありません。");

    private static PokerPlayer GetCurrentPlayer(PokerGame game, ulong userId)
    {
        if (game.CurrentPlayerIndex < 0 || game.CurrentPlayerIndex >= game.Players.Count || game.Players[game.CurrentPlayerIndex].UserId != userId)
            throw new InvalidOperationException("現在の手番ではありません。");
        return game.Players[game.CurrentPlayerIndex];
    }

    private static bool IsFinished(PokerGame game) => game.Phase == PokerPhase.Finished;

    private Task PersistAsync(PokerGame game)
        => _repository == null ? Task.CompletedTask : _repository.SaveAsync(game);

    private static PokerGameSnapshot CreateSnapshot(PokerGame game)
        => new(game.TableId, game.CreatorId, game.CoinRate, game.Phase, game.BetRound, game.Pot, game.CurrentBet,
            game.Players.Select(player => new PokerPlayerSnapshot(player.DisplayName, player.UserId, player.Chips, player.Folded, player.Exchanged, player.CurrentBet, player.ActionHistory.ToArray())).ToArray(),
            game.CurrentPlayerIndex >= 0 && game.CurrentPlayerIndex < game.Players.Count ? game.Players[game.CurrentPlayerIndex].UserId : null,
            game.ActionHistory.ToArray(), game.WinnerText);

    private static IEnumerable<PokerCard> CreateDeck()
    {
        foreach (var suit in Enum.GetValues<PokerSuit>())
            foreach (var rank in Enum.GetValues<PokerRank>())
                yield return new PokerCard(rank, suit);
    }

    private static string CreateTableId()
        => string.Concat(Enumerable.Range(0, 4).Select(_ => "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"[Random.Shared.Next(32)]));
}

public sealed record PokerJoinResult(PokerGame Game, bool Started);
public sealed record PokerActionResult(PokerGame Game, bool Finished);
public sealed record PokerExchangeResult(PokerGame Game, int ExchangedCount);
public sealed record PokerPlayerSnapshot(string DisplayName, ulong UserId, long Chips, bool Folded, bool Exchanged, long CurrentBet, IReadOnlyList<string> Actions);
public sealed record PokerGameSnapshot(string TableId, ulong CreatorId, long CoinRate, PokerPhase Phase, int BetRound, long Pot, long CurrentBet, IReadOnlyList<PokerPlayerSnapshot> Players, ulong? CurrentPlayerId, IReadOnlyList<string> Actions, string? WinnerText);
public sealed record PokerPrivateSnapshot(PokerGame Game, PokerPlayer Player);

internal static class PokerActionExtensions
{
    public static string ToDisplayString(this PokerAction action, long amount) => action switch
    {
        PokerAction.AllIn => "All-in",
        PokerAction.Fold => "Fold",
        PokerAction.Check => "Check",
        PokerAction.Call => $"Call {amount}",
        PokerAction.Bet => $"Bet {amount}",
        PokerAction.Raise => $"Raise {amount}",
        _ => action.ToString()
    };
}
