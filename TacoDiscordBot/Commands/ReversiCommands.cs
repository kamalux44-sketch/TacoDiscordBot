using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using TacoDiscordBot.Models;
using TacoDiscordBot.Services;

namespace TacoDiscordBot.Commands;

public sealed class ReversiCommands : ApplicationCommandModule
{
    private const string CustomIdPrefix = "reversi";
    private const string EmptyCell = "🟪";
    private const string BlackStone = "⚫";
    private const string WhiteStone = "⚪";
    private const string RedCell = "🟥";
    private const string GreenCell = "🟩";
    private static readonly string[] NumberLabels = ["0️⃣", "1️⃣", "2️⃣", "3️⃣", "4️⃣", "5️⃣", "6️⃣", "7️⃣", "8️⃣", "9️⃣", "🔟"];

    [SlashCommand("reversistart", "2人対戦のリバーシを開始します")]
    public async Task Start(
        InteractionContext ctx,
        [Option("bet", "1以上の1人あたりベット額")] long? bet = null
    )
    {
        if (ctx.Guild == null)
        {
            await RespondErrorAsync(ctx, "このコマンドはサーバー内で実行してください。");
            return;
        }

        if (bet.HasValue && bet.Value <= 0)
        {
            await RespondErrorAsync(ctx, "ベット額は正の整数で指定してください。");
            return;
        }
        if (bet.HasValue && BotHost.ReversiBetService == null)
        {
            await RespondErrorAsync(ctx, "コインサービスが利用できないため、ベット対局を開始できません。");
            return;
        }

        try
        {
            var game = BotHost.ReversiService.Start(ctx.Guild.Id, ctx.Channel.Id, ctx.User.Id, bet ?? 0);
            await ctx.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                CreateBuilder(game)
            );
        }
        catch (OverflowException)
        {
            await RespondErrorAsync(ctx, "ベット額が大きすぎます。");
        }
    }

    public static async Task HandleComponentInteractionAsync(
        DiscordClient client,
        ComponentInteractionCreateEventArgs e
    )
    {
        var parts = e.Interaction.Data.CustomId?.Split(':');
        if (parts == null || parts.Length < 3 || parts[0] != CustomIdPrefix)
            return;

        var service = BotHost.ReversiService;
        if (service == null)
            return;

        try
        {
            var gameId = parts[2];
            var game = service.Get(gameId);
            if (game == null)
            {
                await RespondEphemeralAsync(e, "このリバーシゲームは終了しています。");
                return;
            }

            if (parts[1] == "join")
            {
                if (!await service.JoinAsync(gameId, e.Interaction.User.Id, BotHost.ReversiBetService))
                {
                    await RespondEphemeralAsync(e, "ベット開始に必要な残高が不足しています。");
                    return;
                }
            }
            else if (parts[1] == "cancel")
            {
                if (!service.Cancel(gameId, e.Interaction.User.Id))
                {
                    await RespondEphemeralAsync(e, "ゲームを終了できるのは開始者だけです。");
                    return;
                }
            }
            else
            {
                if (parts.Length != 5
                    || !int.TryParse(parts[3], out var boardVersion)
                    || !int.TryParse(parts[4], out var moveId)
                    || moveId < 0 || moveId >= ReversiGame.BoardSize * ReversiGame.BoardSize)
                {
                    return;
                }

                var move = new ReversiMove(moveId / ReversiGame.BoardSize, moveId % ReversiGame.BoardSize);
                if (parts[1] == "promote")
                    service.Promote(gameId, e.Interaction.User.Id, move, boardVersion);
                else if (parts[1] == "play")
                    await service.PlayAndSettleAsync(
                        gameId,
                        e.Interaction.User.Id,
                        move,
                        boardVersion,
                        BotHost.ReversiBetService
                    );
                else
                    return;
            }

            game = service.Get(gameId);
            if (game == null)
                return;

            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.UpdateMessage,
                CreateBuilder(game)
            );
        }
        catch (InvalidOperationException ex)
        {
            await RespondEphemeralAsync(e, ex.Message);
        }
    }

    private static DiscordInteractionResponseBuilder CreateBuilder(ReversiGame game)
    {
        var builder = new DiscordInteractionResponseBuilder()
            .WithContent(CreateContent(game));

        foreach (var row in CreateComponentRows(game))
            builder.AddComponents(row);
        return builder;
    }

    private static string CreateContent(ReversiGame game)
    {
        var lines = new List<string>
        {
            $"**リバーシ** `{game.GameId}`",
            $"黒: {FormatPlayer(game.PlayerBlack)}　白: {FormatPlayer(game.PlayerWhite)}"
        };
        if (game.BetEnabled)
            lines.Add($"💰 ベット：{game.BetAmount}コイン（field: {game.FieldAmount}コイン）");

        if (game.Status == ReversiGameStatus.WaitingForPlayers)
            lines.Add("参加者を2人まで受け付けています。黒が先攻です。");
        else if (game.Status == ReversiGameStatus.Playing)
            lines.Add($"現在のターン: {StoneEmoji(game.CurrentPlayer)} {FormatCurrentPlayer(game)}");
        else if (game.Status == ReversiGameStatus.Finished)
            lines.Add(CreateResultText(game));
        else
            lines.Add("ゲームは終了しました。");

        lines.Add(string.Join('\n', CreateBoardLines(game)));
        return string.Join('\n', lines);
    }

    private static string CreateResultText(ReversiGame game)
    {
        var blackCount = game.Count(ReversiStone.Black);
        var whiteCount = game.Count(ReversiStone.White);
        var result = game.Winner == ReversiStone.Empty
            ? "引き分け"
            : $"{StoneEmoji(game.Winner)} {FormatPlayer(game.Winner == ReversiStone.Black ? game.PlayerBlack : game.PlayerWhite)} の勝利";
        var text = $"ゲーム終了: {result}（黒 {blackCount} - 白 {whiteCount}）";
        if (game.BetSettlement is { } settlement)
        {
            var blackNet = game.Winner == ReversiStone.Black ? settlement.WinnerNet : settlement.LoserNet;
            var whiteNet = game.Winner == ReversiStone.White ? settlement.WinnerNet : settlement.LoserNet;
            text += $"\n倍率：{settlement.Multiplier:0.0}倍\n💰 ベット精算\n⚫ 黒：{blackNet:+#;-#;0}コイン\n⚪ 白：{whiteNet:+#;-#;0}コイン";
        }
        return text;
    }

    private static IEnumerable<string> CreateBoardLines(ReversiGame game)
    {
        var numbered = game.NumberedMoves
            .Select((move, index) => (move.Id, Label: NumberLabels[index]))
            .ToDictionary(item => item.Id, item => item.Label);
        var red = game.RedMoves.Select(move => move.Id).ToHashSet();
        var green = game.GreenMoves.Select(move => move.Id).ToHashSet();

        for (var row = 0; row < ReversiGame.BoardSize; row++)
        {
            var cells = new string[ReversiGame.BoardSize];
            for (var column = 0; column < ReversiGame.BoardSize; column++)
            {
                var moveId = row * ReversiGame.BoardSize + column;
                cells[column] = game.Board[row, column] switch
                {
                    ReversiStone.Black => BlackStone,
                    ReversiStone.White => WhiteStone,
                    _ when numbered.TryGetValue(moveId, out var label) => label,
                    _ when red.Contains(moveId) => RedCell,
                    _ when green.Contains(moveId) => GreenCell,
                    _ => EmptyCell
                };
            }
            yield return string.Concat(cells);
        }
    }

    private static IEnumerable<DiscordComponent[]> CreateComponentRows(ReversiGame game)
    {
        if (game.Status == ReversiGameStatus.WaitingForPlayers)
        {
            yield return
            [
                new DiscordButtonComponent(ButtonStyle.Success, $"{CustomIdPrefix}:join:{game.GameId}", "参加", game.PlayerWhite.HasValue),
                new DiscordButtonComponent(ButtonStyle.Danger, $"{CustomIdPrefix}:cancel:{game.GameId}", "終了")
            ];
            yield break;
        }

        if (game.Status != ReversiGameStatus.Playing)
            yield break;

        var components = new List<DiscordComponent>();
        for (var index = 0; index < game.NumberedMoves.Count; index++)
        {
            var move = game.NumberedMoves[index];
            components.Add(new DiscordButtonComponent(
                ButtonStyle.Primary,
                $"{CustomIdPrefix}:play:{game.GameId}:{game.BoardVersion}:{move.Id}",
                NumberLabels[index]
            ));
        }

        AddPromotionButton(components, game, game.RedMoves, RedCell);
        AddPromotionButton(components, game, game.GreenMoves, GreenCell);
        for (var index = 0; index < components.Count; index += 5)
            yield return components.Skip(index).Take(5).ToArray();
    }

    private static void AddPromotionButton(
        ICollection<DiscordComponent> components,
        ReversiGame game,
        IReadOnlyList<ReversiMove> moves,
        string label
    )
    {
        if (moves.Count == 0)
            return;
        var move = moves[0];
        components.Add(new DiscordButtonComponent(
            ButtonStyle.Secondary,
            $"{CustomIdPrefix}:promote:{game.GameId}:{game.BoardVersion}:{move.Id}",
            label
        ));
    }

    private static string FormatCurrentPlayer(ReversiGame game)
        => game.CurrentPlayer == ReversiStone.Black
            ? FormatPlayer(game.PlayerBlack)
            : FormatPlayer(game.PlayerWhite);

    private static string FormatPlayer(ulong? userId)
        => userId.HasValue ? $"<@{userId.Value}>" : "未参加";

    private static string StoneEmoji(ReversiStone stone)
        => stone == ReversiStone.Black ? BlackStone : WhiteStone;

    private static Task RespondErrorAsync(InteractionContext ctx, string message)
        => ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(message).AsEphemeral(true)
        );

    private static Task RespondEphemeralAsync(ComponentInteractionCreateEventArgs e, string message)
        => e.Interaction.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(message).AsEphemeral(true)
        );
}
