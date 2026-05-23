using System.Collections.Concurrent;
using HexC.Engine;

namespace HexC.Server
{
    public static class GameStore
    {
        // Keyed by lowercase ID for case-insensitive lookup
        private static ConcurrentDictionary<string, Game> _games = new ConcurrentDictionary<string, Game>();
        // Preserves the creator's original casing
        private static ConcurrentDictionary<string, string> _canonicalIds = new ConcurrentDictionary<string, string>();

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
            return game;
        }

        public static bool Exists(string id) => _games.ContainsKey(id.ToLowerInvariant());

        public static string GetCanonicalId(string id)
        {
            _canonicalIds.TryGetValue(id.ToLowerInvariant(), out var canonical);
            return canonical ?? id;
        }
    }
}
