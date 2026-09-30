using System.Collections.Concurrent;
using HexC.Engine;

namespace HexC.Server
{
    /// <summary>
    /// All live games, in memory, keyed by lowercase ID. With a repository attached (production:
    /// HEXC_DATA is set) every create, accepted move and undo is written through, and games are
    /// rebuilt from their move lists at startup. Without one (tests, dev) nothing is persisted.
    /// </summary>
    public static class GameStore
    {
        // Keyed by lowercase ID for case-insensitive lookup
        private static ConcurrentDictionary<string, Game> _games = new ConcurrentDictionary<string, Game>();
        // Preserves the creator's original casing
        private static ConcurrentDictionary<string, string> _canonicalIds = new ConcurrentDictionary<string, string>();
        private static GameRepository? _repo;

        /// <summary>Attaches storage and loads every stored game into memory.</summary>
        public static void Configure(GameRepository repo)
        {
            _repo = repo;
            foreach (var stored in repo.LoadAll())
            {
                _games[stored.Key] = Rebuild(stored.Key, stored.Moves);
                _canonicalIds[stored.Key] = stored.CanonicalId;
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

        public static Game Create(string id)
        {
            var key = id.ToLowerInvariant();
            var game = new Game();
            _games[key] = game;
            _canonicalIds[key] = id;
            _repo?.CreateGame(key, id);
            return game;
        }

        /// <summary>
        /// Submits a move and reports whether it was accepted. Game.SubmitMove returns void and
        /// signals failure only through StatusMessage, so success is inferred from state changes.
        /// </summary>
        public static bool TrySubmitMove(string id, Game game, int q1, int r1, int q2, int r2)
        {
            lock (game)
            {
                if (!Apply(game, q1, r1, q2, r2)) return false;
                _repo?.AppendMove(id.ToLowerInvariant(), q1, r1, q2, r2);
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
