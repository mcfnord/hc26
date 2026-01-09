using System.Collections.Concurrent;
using HexC.Engine;

namespace HexC.Server
{
    public static class GameStore
    {
        // Thread-safe dictionary to hold multiple games
        // Key = GameID, Value = The Game Object
        private static ConcurrentDictionary<string, Game> _games = new ConcurrentDictionary<string, Game>();

        public static Game Get(string id)
        {
            if (_games.TryGetValue(id, out var game))
                return game;
            return null;
        }

        public static Game Create(string id)
        {
            var game = new Game(); 
            _games[id] = game;
            return game;
        }

        public static bool Exists(string id) => _games.ContainsKey(id);
    }
}