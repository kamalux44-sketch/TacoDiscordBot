using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using TacoDiscordBot.Models;

namespace TacoDiscordBot.Repository;

public sealed class PokerRepository
{
    private readonly BaseRepository _base;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PokerRepository(BaseRepository baseRepository)
    {
        _base = baseRepository ?? throw new ArgumentNullException(nameof(baseRepository));
    }

    public Task EnsureTablesExistAsync()
        => _base.ExecuteNonQueryAsync("""
            CREATE TABLE IF NOT EXISTS poker_games (
                table_id VARCHAR(4) PRIMARY KEY,
                guild_id BIGINT NOT NULL,
                creator_id BIGINT NOT NULL,
                coin_rate BIGINT NOT NULL CHECK (coin_rate > 0),
                channel_id BIGINT NOT NULL,
                public_message_id BIGINT NULL,
                pot BIGINT NOT NULL DEFAULT 0 CHECK (pot >= 0),
                current_bet BIGINT NOT NULL DEFAULT 0 CHECK (current_bet >= 0),
                bet_round INTEGER NOT NULL DEFAULT 1,
                phase INTEGER NOT NULL,
                current_player_index INTEGER NOT NULL DEFAULT 0,
                winner_text TEXT NULL,
                settled BOOLEAN NOT NULL DEFAULT FALSE,
                deck_json JSONB NOT NULL,
                action_history_json JSONB NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE TABLE IF NOT EXISTS poker_players (
                table_id VARCHAR(4) NOT NULL REFERENCES poker_games(table_id) ON DELETE CASCADE,
                user_id BIGINT NOT NULL,
                display_name TEXT NOT NULL,
                chips BIGINT NOT NULL CHECK (chips >= 0),
                folded BOOLEAN NOT NULL DEFAULT FALSE,
                exchanged BOOLEAN NOT NULL DEFAULT FALSE,
                current_bet BIGINT NOT NULL DEFAULT 0 CHECK (current_bet >= 0),
                all_in BOOLEAN NOT NULL DEFAULT FALSE,
                hand_json JSONB NOT NULL,
                action_history_json JSONB NOT NULL,
                PRIMARY KEY (table_id, user_id)
            );
            """);

    public async Task SaveAsync(PokerGame game)
    {
        await _base.UseNpgsqlTransactionAsync(async (connection, transaction) =>
        {
            await using (var command = new NpgsqlCommand("""
                INSERT INTO poker_games (
                    table_id, guild_id, creator_id, coin_rate, channel_id, public_message_id,
                    pot, current_bet, bet_round, phase, current_player_index, winner_text,
                    settled, deck_json, action_history_json, updated_at
                ) VALUES (
                    @table_id, @guild_id, @creator_id, @coin_rate, @channel_id, @public_message_id,
                    @pot, @current_bet, @bet_round, @phase, @current_player_index, @winner_text,
                    @settled, @deck_json, @action_history_json, now()
                )
                ON CONFLICT (table_id) DO UPDATE SET
                    guild_id = EXCLUDED.guild_id,
                    creator_id = EXCLUDED.creator_id,
                    coin_rate = EXCLUDED.coin_rate,
                    channel_id = EXCLUDED.channel_id,
                    public_message_id = EXCLUDED.public_message_id,
                    pot = EXCLUDED.pot,
                    current_bet = EXCLUDED.current_bet,
                    bet_round = EXCLUDED.bet_round,
                    phase = EXCLUDED.phase,
                    current_player_index = EXCLUDED.current_player_index,
                    winner_text = EXCLUDED.winner_text,
                    settled = EXCLUDED.settled,
                    deck_json = EXCLUDED.deck_json,
                    action_history_json = EXCLUDED.action_history_json,
                    updated_at = now();
                """, connection, transaction))
            {
                AddGameParameters(command, game);
                await command.ExecuteNonQueryAsync();
            }

            await using (var deleteCommand = new NpgsqlCommand(
                "DELETE FROM poker_players WHERE table_id = @table_id;", connection, transaction))
            {
                deleteCommand.Parameters.AddWithValue("table_id", game.TableId);
                await deleteCommand.ExecuteNonQueryAsync();
            }

            foreach (var player in game.Players)
            {
                await using var playerCommand = new NpgsqlCommand("""
                    INSERT INTO poker_players (
                        table_id, user_id, display_name, chips, folded, exchanged,
                        current_bet, all_in, hand_json, action_history_json
                    ) VALUES (
                        @table_id, @user_id, @display_name, @chips, @folded, @exchanged,
                        @current_bet, @all_in, @hand_json, @action_history_json
                    );
                    """, connection, transaction);
                playerCommand.Parameters.AddWithValue("table_id", game.TableId);
                playerCommand.Parameters.AddWithValue("user_id", (long)player.UserId);
                playerCommand.Parameters.AddWithValue("display_name", player.DisplayName);
                playerCommand.Parameters.AddWithValue("chips", player.Chips);
                playerCommand.Parameters.AddWithValue("folded", player.Folded);
                playerCommand.Parameters.AddWithValue("exchanged", player.Exchanged);
                playerCommand.Parameters.AddWithValue("current_bet", player.CurrentBet);
                playerCommand.Parameters.AddWithValue("all_in", player.AllIn);
                AddJsonParameter(playerCommand, "hand_json", player.Hand);
                AddJsonParameter(playerCommand, "action_history_json", player.ActionHistory);
                await playerCommand.ExecuteNonQueryAsync();
            }

            return true;
        });
    }

    public async Task<IReadOnlyList<PokerGame>> LoadUnsettledAsync()
    {
        var games = new Dictionary<string, PokerGame>(StringComparer.OrdinalIgnoreCase);
        await _base.UseNpgsqlTransactionAsync(async (connection, transaction) =>
        {
            await using (var gameCommand = new NpgsqlCommand("""
                SELECT table_id, guild_id, creator_id, coin_rate, channel_id, public_message_id,
                       pot, current_bet, bet_round, phase, current_player_index, winner_text,
                       settled, deck_json, action_history_json
                FROM poker_games
                WHERE settled = FALSE;
                """, connection, transaction))
            await using (var reader = await gameCommand.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var game = new PokerGame(
                        reader.GetString(0),
                        (ulong)reader.GetInt64(1),
                        (ulong)reader.GetInt64(2),
                        reader.GetInt64(3),
                        (ulong)reader.GetInt64(4))
                    {
                        PublicMessageId = reader.IsDBNull(5) ? null : (ulong?)reader.GetInt64(5),
                        Pot = reader.GetInt64(6),
                        CurrentBet = reader.GetInt64(7),
                        BetRound = reader.GetInt32(8),
                        Phase = (PokerPhase)reader.GetInt32(9),
                        CurrentPlayerIndex = reader.GetInt32(10),
                        WinnerText = reader.IsDBNull(11) ? null : reader.GetString(11),
                        Settled = reader.GetBoolean(12),
                        Deck = DeserializeQueue<PokerCard>(reader.GetString(13)),
                    };
                    game.ActionHistory.AddRange(Deserialize<List<string>>(reader.GetString(14)));
                    games.Add(game.TableId, game);
                }
            }

            await using (var playerCommand = new NpgsqlCommand("""
                SELECT table_id, user_id, display_name, chips, folded, exchanged,
                       current_bet, all_in, hand_json, action_history_json
                FROM poker_players
                WHERE table_id = ANY(@table_ids)
                ORDER BY table_id, user_id;
                """, connection, transaction))
            {
                playerCommand.Parameters.AddWithValue("table_ids", games.Keys.ToArray());
                await using var reader = await playerCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (!games.TryGetValue(reader.GetString(0), out var game))
                        continue;
                    var player = new PokerPlayer((ulong)reader.GetInt64(1), reader.GetString(2), reader.GetInt64(3))
                    {
                        Folded = reader.GetBoolean(4),
                        Exchanged = reader.GetBoolean(5),
                        CurrentBet = reader.GetInt64(6),
                        AllIn = reader.GetBoolean(7)
                    };
                    player.Hand.AddRange(Deserialize<List<PokerCard>>(reader.GetString(8)));
                    player.ActionHistory.AddRange(Deserialize<List<string>>(reader.GetString(9)));
                    game.Players.Add(player);
                }
            }

            return true;
        });
        return games.Values.ToArray();
    }

    private static void AddGameParameters(NpgsqlCommand command, PokerGame game)
    {
        command.Parameters.AddWithValue("table_id", game.TableId);
        command.Parameters.AddWithValue("guild_id", (long)game.GuildId);
        command.Parameters.AddWithValue("creator_id", (long)game.CreatorId);
        command.Parameters.AddWithValue("coin_rate", game.CoinRate);
        command.Parameters.AddWithValue("channel_id", (long)game.ChannelId);
        command.Parameters.AddWithValue(
            "public_message_id",
            game.PublicMessageId.HasValue
                ? (object)(long)game.PublicMessageId.Value
                : DBNull.Value);
        command.Parameters.AddWithValue("pot", game.Pot);
        command.Parameters.AddWithValue("current_bet", game.CurrentBet);
        command.Parameters.AddWithValue("bet_round", game.BetRound);
        command.Parameters.AddWithValue("phase", (int)game.Phase);
        command.Parameters.AddWithValue("current_player_index", game.CurrentPlayerIndex);
        command.Parameters.AddWithValue("winner_text", (object?)game.WinnerText ?? DBNull.Value);
        command.Parameters.AddWithValue("settled", game.Settled);
        AddJsonParameter(command, "deck_json", game.Deck.ToArray());
        AddJsonParameter(command, "action_history_json", game.ActionHistory);
    }

    private static void AddJsonParameter<T>(NpgsqlCommand command, string name, T value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(value, JsonOptions)
        });

    private static T Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException("Poker状態のJSONを復元できませんでした。");

    private static Queue<T> DeserializeQueue<T>(string json)
        => new(Deserialize<List<T>>(json));
}
