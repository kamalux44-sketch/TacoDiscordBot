using System.Collections.Concurrent;
using TacoDiscordBot.Models;

namespace TacoDiscordBot.Services;

public sealed class TexasPokerService
{
    private readonly ConcurrentDictionary<string, TexasPokerGame> _games = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random = new();

    public TexasPokerGame CreateGame(ulong guildId, ulong hostId, ulong channelId)
    {
        var game = new TexasPokerGame(Guid.NewGuid().ToString("N"), guildId, hostId, channelId);
        if (!_games.TryAdd(game.GameId, game))
            throw new InvalidOperationException("Texas Hold'emの卓を作成できませんでした。");
        return game;
    }

    public TexasPokerGame? Find(string gameId)
        => _games.TryGetValue(gameId, out var game) ? game : null;

    public void SetPublicMessageId(string gameId, ulong messageId)
    {
        var game = GetGame(gameId);
        lock (game)
        {
            game.PublicMessageId = messageId;
        }
    }

    public TexasPokerPlayer Join(string gameId, ulong userId, string displayName)
    {
        var game = GetGame(gameId);
        lock (game)
        {
            EnsurePhase(game, TexasPokerPhase.Waiting);
            if (game.Players.Any(player => player.UserId == userId))
                throw new InvalidOperationException("既に参加しています。");
            if (game.Players.Count >= TexasPokerGame.MaxPlayers)
                throw new InvalidOperationException("この卓は満員です。");

            var player = new TexasPokerPlayer(userId, displayName);
            game.Players.Add(player);
            return player;
        }
    }

    public TexasPokerGame Start(string gameId, ulong userId)
    {
        var game = GetGame(gameId);
        lock (game)
        {
            EnsurePhase(game, TexasPokerPhase.Waiting);
            if (game.HostId != userId)
                throw new InvalidOperationException("このゲームの開始権限がありません。");
            if (game.Players.Count < TexasPokerGame.MinPlayers)
                throw new InvalidOperationException("ゲームを開始するには2人以上の参加が必要です。");

            BuildDeck(game);
            game.DealerIndex = 0;
            foreach (var player in game.Players)
            {
                player.HoleCards.Add(game.Deck.Dequeue());
                player.HoleCards.Add(game.Deck.Dequeue());
                player.State = TexasPokerPlayerState.Waiting;
            }

            game.Phase = TexasPokerPhase.PreFlop;
            PostBlind(game, NextPlayerIndex(game.DealerIndex, game.Players.Count), TexasPokerGame.SmallBlind);
            PostBlind(game, NextPlayerIndex(NextPlayerIndex(game.DealerIndex, game.Players.Count), game.Players.Count), TexasPokerGame.BigBlind);
            game.CurrentBet = TexasPokerGame.BigBlind;
            game.CurrentPlayerIndex = NextActiveIndex(game, NextPlayerIndex(NextPlayerIndex(game.DealerIndex, game.Players.Count), game.Players.Count));
            game.Players[game.CurrentPlayerIndex].State = TexasPokerPlayerState.Acting;
            return game;
        }
    }

    public void Cancel(string gameId, ulong userId)
    {
        var game = GetGame(gameId);
        lock (game)
        {
            EnsurePhase(game, TexasPokerPhase.Waiting);
            if (game.HostId != userId)
                throw new InvalidOperationException("このゲームのキャンセル権限がありません。");
            game.Phase = TexasPokerPhase.Cancelled;
            game.ResultText = "ゲームはキャンセルされました。";
        }
    }

    public TexasPokerGame ApplyAction(string gameId, ulong userId, TexasPokerAction action, long amount = 0)
    {
        var game = GetGame(gameId);
        lock (game)
        {
            if (game.Phase is TexasPokerPhase.Waiting or TexasPokerPhase.Finished or TexasPokerPhase.Cancelled)
                throw new InvalidOperationException("現在アクションを受け付けていません。");
            if (game.CurrentPlayerIndex < 0 || game.Players[game.CurrentPlayerIndex].UserId != userId)
                throw new InvalidOperationException("現在の手番ではありません。");

            var player = game.Players[game.CurrentPlayerIndex];
            var callAmount = game.CurrentBet - player.CurrentBet;
            switch (action)
            {
                case TexasPokerAction.Check when callAmount != 0:
                    throw new InvalidOperationException("ベットがあるためチェックできません。");
                case TexasPokerAction.Call when callAmount <= 0:
                    throw new InvalidOperationException("コールするベットがありません。");
                case TexasPokerAction.Bet when game.CurrentBet != 0:
                    throw new InvalidOperationException("既にベットがあるためベットできません。");
                case TexasPokerAction.Bet:
                    ValidateAmount(player, amount);
                    PutChips(game, player, amount);
                    game.CurrentBet = player.CurrentBet;
                    ResetOtherActingStates(game, player);
                    break;
                case TexasPokerAction.Raise:
                    ValidateAmount(player, amount);
                    if (amount <= game.CurrentBet)
                        throw new InvalidOperationException("レイズ額は現在のベットを超える必要があります。");
                    PutChips(game, player, amount - player.CurrentBet);
                    game.CurrentBet = amount;
                    ResetOtherActingStates(game, player);
                    break;
                case TexasPokerAction.Call:
                    PutChips(game, player, callAmount);
                    break;
                case TexasPokerAction.Fold:
                    player.IsFolded = true;
                    player.State = TexasPokerPlayerState.Folded;
                    break;
                case TexasPokerAction.Check:
                    player.State = TexasPokerPlayerState.Checked;
                    break;
                default:
                    throw new InvalidOperationException("指定されたアクションは実行できません。");
            }

            if (!player.IsFolded && action is not TexasPokerAction.Check)
                player.State = player.IsAllIn ? TexasPokerPlayerState.AllIn : TexasPokerPlayerState.Called;

            if (game.Players.Count(p => !p.IsFolded) == 1)
            {
                FinishByFold(game);
                return game;
            }

            if (game.Players.Where(p => !p.IsFolded).All(p => p.IsAllIn))
            {
                RunAllInToShowdown(game);
                return game;
            }

            var next = NextActiveIndex(game, game.CurrentPlayerIndex);
            if (next == game.CurrentPlayerIndex || AllActed(game))
            {
                AdvanceRound(game);
            }
            else
            {
                game.Players[game.CurrentPlayerIndex].State = player.IsFolded ? TexasPokerPlayerState.Folded : player.State;
                game.CurrentPlayerIndex = next;
                game.Players[next].State = TexasPokerPlayerState.Acting;
            }
            return game;
        }
    }

    public TexasPokerPlayer GetPlayer(string gameId, ulong userId)
    {
        var game = GetGame(gameId);
        lock (game)
        {
            return game.Players.FirstOrDefault(player => player.UserId == userId)
                ?? throw new InvalidOperationException("この卓の参加者ではありません。");
        }
    }

    private static void ResetOtherActingStates(TexasPokerGame game, TexasPokerPlayer actingPlayer)
    {
        foreach (var player in game.Players.Where(player => player != actingPlayer && !player.IsFolded && !player.IsAllIn))
            player.State = TexasPokerPlayerState.Waiting;
    }

    private void AdvanceRound(TexasPokerGame game)
    {
        foreach (var player in game.Players)
        {
            player.CurrentBet = 0;
            if (!player.IsFolded)
                player.State = TexasPokerPlayerState.Waiting;
        }
        game.CurrentBet = 0;
        game.CurrentPlayerIndex = -1;
        if (game.Phase == TexasPokerPhase.PreFlop)
        {
            AddCommunityCards(game, 3);
            game.Phase = TexasPokerPhase.Flop;
        }
        else if (game.Phase == TexasPokerPhase.Flop)
        {
            AddCommunityCards(game, 1);
            game.Phase = TexasPokerPhase.Turn;
        }
        else if (game.Phase == TexasPokerPhase.Turn)
        {
            AddCommunityCards(game, 1);
            game.Phase = TexasPokerPhase.River;
        }
        else
        {
            game.Phase = TexasPokerPhase.Showdown;
            FinishByShowdown(game);
            return;
        }
        game.CurrentPlayerIndex = NextActiveIndex(game, game.DealerIndex);
        if (game.Players.Where(player => !player.IsFolded).All(player => player.IsAllIn))
        {
            RunAllInToShowdown(game);
            return;
        }
        game.Players[game.CurrentPlayerIndex].State = TexasPokerPlayerState.Acting;
    }

    private static bool AllActed(TexasPokerGame game)
    {
        var active = game.Players.Where(player => !player.IsFolded && !player.IsAllIn).ToList();
        return active.Count == 0 || active.All(player => player.State is TexasPokerPlayerState.Called or TexasPokerPlayerState.Checked);
    }

    private static void FinishByFold(TexasPokerGame game)
    {
        var winner = game.Players.Single(player => !player.IsFolded);
        game.WinnerUserIds.Clear();
        game.WinnerUserIds.Add(winner.UserId);
        winner.Chips += game.Pot;
        winner.State = TexasPokerPlayerState.Winner;
        game.ResultText = $"{winner.DisplayName} の勝利（他のプレイヤーがフォールド）";
        game.Pot = 0;
        game.Phase = TexasPokerPhase.Finished;
        game.CurrentPlayerIndex = -1;
    }

    private static void FinishByShowdown(TexasPokerGame game)
    {
        var eligible = game.Players.Where(player => !player.IsFolded).ToList();
        var evaluations = eligible.ToDictionary(player => player, player => EvaluateBest(player, game));
        game.HandRankNames.Clear();
        foreach (var result in evaluations)
            game.HandRankNames[result.Key.UserId] = result.Value.CategoryDisplayName;

        var best = evaluations.Values.Max()!;
        var winners = evaluations.Where(result => result.Value.CompareTo(best) == 0).Select(result => result.Key).ToList();
        game.WinnerUserIds.Clear();
        game.WinnerUserIds.AddRange(winners.Select(player => player.UserId));
        var share = game.Pot / winners.Count;
        var remainder = game.Pot % winners.Count;
        for (var index = 0; index < winners.Count; index++)
        {
            winners[index].Chips += share + (index < remainder ? 1 : 0);
            winners[index].State = TexasPokerPlayerState.Winner;
        }
        foreach (var player in eligible.Where(player => !winners.Contains(player)))
            player.State = TexasPokerPlayerState.Loser;
        var winnerNames = string.Join("、", winners.Select(player => player.DisplayName));
        game.ResultText = winners.Count == 1
            ? $"{winnerNames} の勝利（{best.CategoryDisplayName}）"
            : $"{winnerNames} の引き分け（{best.CategoryDisplayName}、ポット分割）";
        game.Pot = 0;
        game.Phase = TexasPokerPhase.Finished;
        game.CurrentPlayerIndex = -1;
    }

    private void RunAllInToShowdown(TexasPokerGame game)
    {
        while (game.Phase is TexasPokerPhase.PreFlop or TexasPokerPhase.Flop or TexasPokerPhase.Turn)
            AdvanceRound(game);
        if (game.Phase == TexasPokerPhase.River)
        {
            game.Phase = TexasPokerPhase.Showdown;
            FinishByShowdown(game);
        }
    }

    public PokerHandEvaluation? GetHandEvaluation(string gameId, ulong userId)
    {
        var game = GetGame(gameId);
        lock (game)
        {
            var player = game.Players.FirstOrDefault(candidate => candidate.UserId == userId);
            if (player == null || game.CommunityCards.Count < 3)
                return null;
            return EvaluateBest(player, game);
        }
    }

    private static PokerHandEvaluation EvaluateBest(TexasPokerPlayer player, TexasPokerGame game)
    {
        var cards = player.HoleCards.Concat(game.CommunityCards).ToList();
        PokerHandEvaluation? best = null;
        for (var a = 0; a < cards.Count - 4; a++)
        for (var b = a + 1; b < cards.Count - 3; b++)
        for (var c = b + 1; c < cards.Count - 2; c++)
        for (var d = c + 1; d < cards.Count - 1; d++)
        for (var e = d + 1; e < cards.Count; e++)
        {
            var evaluation = PokerHandEvaluator.Evaluate([cards[a], cards[b], cards[c], cards[d], cards[e]]);
            if (best == null || evaluation.CompareTo(best) > 0)
                best = evaluation;
        }
        return best!;
    }

    private void BuildDeck(TexasPokerGame game)
    {
        var cards = from suit in Enum.GetValues<PokerSuit>() from rank in Enum.GetValues<PokerRank>() select new PokerCard(rank, suit);
        foreach (var card in cards.OrderBy(_ => _random.Next()))
            game.Deck.Enqueue(card);
    }

    private static void AddCommunityCards(TexasPokerGame game, int count)
    {
        for (var index = 0; index < count; index++)
            game.CommunityCards.Add(game.Deck.Dequeue());
    }

    private static void PostBlind(TexasPokerGame game, int index, long amount)
        => PutChips(game, game.Players[index], amount);

    private static void PutChips(TexasPokerGame game, TexasPokerPlayer player, long amount)
    {
        if (amount <= 0 || amount > player.Chips)
            throw new InvalidOperationException("チップが不足しています。");
        player.Chips -= amount;
        player.CurrentBet += amount;
        game.Pot += amount;
        if (player.Chips == 0)
            player.IsAllIn = true;
    }

    private static void ValidateAmount(TexasPokerPlayer player, long amount)
    {
        if (amount <= 0 || amount > player.Chips)
            throw new InvalidOperationException("有効なベット額を指定してください。");
    }

    private static int NextPlayerIndex(int index, int count)
        => (index + 1) % count;

    private static int NextActiveIndex(TexasPokerGame game, int start)
    {
        for (var offset = 1; offset <= game.Players.Count; offset++)
        {
            var index = (start + offset) % game.Players.Count;
            if (!game.Players[index].IsFolded && !game.Players[index].IsAllIn)
                return index;
        }
        return start;
    }

    private static void EnsurePhase(TexasPokerGame game, TexasPokerPhase phase)
    {
        if (game.Phase != phase)
            throw new InvalidOperationException("この操作は現在のゲーム状態では実行できません。");
    }

    private TexasPokerGame GetGame(string gameId)
        => Find(gameId) ?? throw new InvalidOperationException("指定されたTexas Hold'em卓は存在しません。");
}
