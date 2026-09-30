using System.Collections.Concurrent;
using HexC.AI;
using HexC.Engine;

namespace HexC.Server
{
    public class Seat
    {
        public ColorsEnum Color { get; init; }
        public string? UserId { get; init; }
        public string? UserName { get; init; }
        public bool IsAi => UserId == null;
    }

    /// <summary>
    /// What the engine doesn't know about a game: whether it is a table (PLAN.md 2026-09-30
    /// landing flow), who sits where, and its clocks. Anonymous games are not tables and have no seats.
    /// </summary>
    public class GameMeta
    {
        public string Key { get; init; } = "";
        public bool IsTable { get; init; }
        public DateTime? StartedUtc { get; set; }
        public DateTime? CountdownEndsUtc { get; set; }
        public DateTime? LastMoveUtc { get; set; }
        // Replaced as a whole on every change, so readers never see a list mid-edit.
        private volatile List<Seat> _seats = new();
        public IReadOnlyList<Seat> Seats => _seats;
        internal void ReplaceSeats(List<Seat> seats) => _seats = seats;

        public bool Waiting => IsTable && StartedUtc == null;
        public int Humans => Seats.Count(s => !s.IsAi);
        public Seat? SeatOf(ColorsEnum color) => Seats.FirstOrDefault(s => s.Color == color);
        public Seat? SeatOfUser(string userId) => Seats.FirstOrDefault(s => s.UserId == userId);
    }

    /// <summary>
    /// All live games, in memory, keyed by lowercase ID. With a repository attached (production:
    /// HEXC_DATA is set) every create, accepted move and undo is written through, and games are
    /// rebuilt from their move lists at startup. Without one (tests, dev) nothing is persisted.
    /// Also the lobby: one waiting table at a time, seats in arrival order, a countdown once two
    /// humans are seated, and a turn clock after which the AI moves for an absent human.
    /// </summary>
    public static class GameStore
    {
        // Landing flow (PLAN.md 2026-09-30). Once a second human sits down, AI takes the third
        // seat when the countdown runs out. If it stays a human's turn for the whole turn clock,
        // the AI moves for them so the others are never stuck.
        public static readonly TimeSpan Countdown = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan TurnClock = TimeSpan.FromHours(1);
        private static readonly ColorsEnum[] SeatOrder = { ColorsEnum.Blue, ColorsEnum.White, ColorsEnum.Red };

        // Keyed by lowercase ID for case-insensitive lookup
        private static ConcurrentDictionary<string, Game> _games = new ConcurrentDictionary<string, Game>();
        // Preserves the creator's original casing
        private static ConcurrentDictionary<string, string> _canonicalIds = new ConcurrentDictionary<string, string>();
        private static ConcurrentDictionary<string, GameMeta> _meta = new ConcurrentDictionary<string, GameMeta>();
        private static GameRepository? _repo;
        private static readonly object _lobbyLock = new();

        /// <summary>Attaches storage and loads every stored game into memory.</summary>
        public static void Configure(GameRepository repo)
        {
            _repo = repo;
            foreach (var stored in repo.LoadAll())
            {
                _games[stored.Key] = Rebuild(stored.Key, stored.Moves);
                _canonicalIds[stored.Key] = stored.CanonicalId;
                var meta = new GameMeta
                {
                    Key = stored.Key, IsTable = stored.IsTable,
                    StartedUtc = stored.StartedUtc, CountdownEndsUtc = stored.CountdownEndsUtc, LastMoveUtc = stored.LastMoveUtc
                };
                meta.ReplaceSeats(stored.Seats
                    .Select(s => new Seat { Color = Enum.Parse<ColorsEnum>(s.Color), UserId = s.UserId, UserName = s.UserName })
                    .ToList());
                _meta[stored.Key] = meta;
            }
        }

        /// <summary>Replays a move list on a fresh game. Public so persistence can be tested without the static store.</summary>
        public static Game Rebuild(string key, IEnumerable<(int Q1, int R1, int Q2, int R2)> moves)
        {
            var game = new Game();
            int n = 0;
            foreach (var m in moves)
            {
                n++;
                if (!Apply(game, m.Q1, m.R1, m.Q2, m.R2))
                    Console.Error.WriteLine($"GameStore: game '{key}' move {n} ({m.Q1},{m.R1})->({m.Q2},{m.R2}) did not replay: {game.StatusMessage}");
            }
            return game;
        }

        public static Game Get(string id)
        {
            if (_games.TryGetValue(id.ToLowerInvariant(), out var game))
                return game;
            return null;
        }

        public static GameMeta? Meta(string id) =>
            _meta.TryGetValue(id.ToLowerInvariant(), out var meta) ? meta : null;

        /// <summary>Creates (or resets) an anonymous game: no seats, the browser plays Blue and drives the AIs.</summary>
        public static Game Create(string id)
        {
            var key = id.ToLowerInvariant();
            var game = new Game();
            _games[key] = game;
            _canonicalIds[key] = id;
            _meta[key] = new GameMeta { Key = key, IsTable = false };
            _repo?.CreateGame(key, id);
            return game;
        }

        public static void RecordUser(string id, string? email, string? name, string? picture) =>
            _repo?.UpsertUser(id, email, name, picture);

        /// <summary>
        /// Where a signed-in person belongs: the table or unfinished game they are seated at, else
        /// a seat at the one waiting table (created if there is none). Idempotent.
        /// </summary>
        public static GameMeta Enter(string userId, string userName, DateTime now)
        {
            lock (_lobbyLock)
            {
                foreach (var m in _meta.Values.Where(m => m.IsTable && m.SeatOfUser(userId) != null))
                    if (m.Waiting || _games[m.Key].State != GameStateEnum.Finished) return m;

                var table = _meta.Values.FirstOrDefault(m => m.Waiting) ?? CreateTable();
                var color = SeatOrder.First(c => table.SeatOf(c) == null);
                SetSeat(table, new Seat { Color = color, UserId = userId, UserName = userName });

                if (table.Humans >= 3) Start(table, now);
                else if (table.Humans == 2) { table.CountdownEndsUtc = now + Countdown; SaveState(table); }
                else { table.CountdownEndsUtc = null; SaveState(table); }
                return table;
            }
        }

        /// <summary>Leaves the waiting table (signing out). A table nobody is left at is removed.</summary>
        public static bool Leave(string userId)
        {
            lock (_lobbyLock)
            {
                var table = _meta.Values.FirstOrDefault(m => m.Waiting && m.SeatOfUser(userId) != null);
                if (table == null) return false;
                var seat = table.SeatOfUser(userId)!;
                table.ReplaceSeats(table.Seats.Where(s => s != seat).ToList());
                _repo?.ClearSeat(table.Key, seat.Color.ToString());
                if (table.Humans == 0)
                {
                    _games.TryRemove(table.Key, out _);
                    _meta.TryRemove(table.Key, out _);
                    _canonicalIds.TryRemove(table.Key, out _);
                    _repo?.DeleteGame(table.Key);
                }
                else
                {
                    table.CountdownEndsUtc = null;   // back to one human: no countdown
                    SaveState(table);
                }
                return true;
            }
        }

        /// <summary>The server clock: starts tables whose countdown ran out and moves for absent humans.</summary>
        public static void Tick(DateTime now)
        {
            lock (_lobbyLock)
            {
                foreach (var table in _meta.Values.Where(m => m.IsTable).ToList())
                {
                    if (table.Waiting)
                    {
                        if (table.CountdownEndsUtc != null && now >= table.CountdownEndsUtc) Start(table, now);
                        continue;
                    }
                    if (!_games.TryGetValue(table.Key, out var game) || game.State == GameStateEnum.Finished) continue;
                    var seat = table.SeatOf(game.CurrentTurn);
                    if (seat == null || seat.IsAi || table.LastMoveUtc == null || now - table.LastMoveUtc < TurnClock) continue;

                    var move = new BasicBot(game.CurrentTurn).PickMove(game.Board);
                    if (move != null && TrySubmitMove(table.Key, game, move.Q1, move.R1, move.Q2, move.R2, now))
                        Console.WriteLine($"GameStore: table '{table.Key}': {game.CurrentTurn} was away for over {TurnClock}, AI moved for {seat.UserName}.");
                }
            }
        }

        private static void Start(GameMeta table, DateTime now)
        {
            foreach (var color in SeatOrder)
                if (table.SeatOf(color) == null)
                    SetSeat(table, new Seat { Color = color, UserId = null, UserName = "AI" });
            table.StartedUtc = now;
            table.CountdownEndsUtc = null;
            table.LastMoveUtc = now;
            SaveState(table);
        }

        private static GameMeta CreateTable()
        {
            string key;
            do { key = "t" + new string(Enumerable.Range(0, 10).Select(_ => (char)('a' + Random.Shared.Next(26))).ToArray()); }
            while (_games.ContainsKey(key));
            _games[key] = new Game();
            _canonicalIds[key] = key;
            var meta = new GameMeta { Key = key, IsTable = true };
            _meta[key] = meta;
            _repo?.CreateGame(key, key, isTable: true);
            return meta;
        }

        private static void SetSeat(GameMeta table, Seat seat)
        {
            var seats = table.Seats.Where(s => s.Color != seat.Color).ToList();
            seats.Add(seat);
            table.ReplaceSeats(seats);
            _repo?.SetSeat(table.Key, seat.Color.ToString(), seat.UserId, seat.UserName);
        }

        private static void SaveState(GameMeta table) =>
            _repo?.SaveTableState(table.Key, table.StartedUtc, table.CountdownEndsUtc, table.LastMoveUtc);

        /// <summary>
        /// Submits a move and reports whether it was accepted. Game.SubmitMove returns void and
        /// signals failure only through StatusMessage, so success is inferred from state changes.
        /// </summary>
        public static bool TrySubmitMove(string id, Game game, int q1, int r1, int q2, int r2, DateTime? now = null)
        {
            lock (game)
            {
                if (!Apply(game, q1, r1, q2, r2)) return false;
                var key = id.ToLowerInvariant();
                _repo?.AppendMove(key, q1, r1, q2, r2);
                if (_meta.TryGetValue(key, out var meta) && meta.IsTable)
                {
                    meta.LastMoveUtc = now ?? DateTime.UtcNow;
                    SaveState(meta);
                }
                return true;
            }
        }

        public static bool TryTakeBack(string id, Game game)
        {
            lock (game)
            {
                if (!game.TakeBack()) return false;
                _repo?.RemoveLastMove(id.ToLowerInvariant());
                return true;
            }
        }

        // Turn changed, game just ended, or a Diddilydoo swap toggled MainMovePending.
        private static bool Apply(Game game, int q1, int r1, int q2, int r2)
        {
            var turnBefore = game.CurrentTurn;
            var stateBefore = game.State;
            var pendingBefore = game.MainMovePending;
            game.SubmitMove(q1, r1, q2, r2);
            return game.CurrentTurn != turnBefore
                || (game.State == GameStateEnum.Finished && stateBefore != GameStateEnum.Finished)
                || game.MainMovePending != pendingBefore;
        }

        public static bool Exists(string id) => _games.ContainsKey(id.ToLowerInvariant());

        public static string GetCanonicalId(string id)
        {
            _canonicalIds.TryGetValue(id.ToLowerInvariant(), out var canonical);
            return canonical ?? id;
        }
    }
}
