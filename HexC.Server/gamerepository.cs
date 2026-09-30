using Microsoft.Data.Sqlite;

namespace HexC.Server
{
    /// <summary>
    /// SQLite storage for games as move lists (PLAN.md: Persistence). A game is its ID plus the
    /// ordered coordinates of every accepted SubmitMove; replaying them on a fresh Game rebuilds
    /// the exact state, undo stack included. Deploys back this file up and reverts restore it.
    /// </summary>
    public sealed class GameRepository : IDisposable
    {
        public record StoredGame(string Key, string CanonicalId, List<(int Q1, int R1, int Q2, int R2)> Moves);

        private readonly SqliteConnection _conn;
        private readonly object _lock = new();

        public GameRepository(string path)
        {
            _conn = new SqliteConnection($"Data Source={path}");
            _conn.Open();
            Exec("PRAGMA journal_mode=WAL");
            Exec("PRAGMA synchronous=NORMAL");
            Exec(@"CREATE TABLE IF NOT EXISTS games (
                       key TEXT PRIMARY KEY,
                       canonical_id TEXT NOT NULL,
                       created_utc TEXT NOT NULL);
                   CREATE TABLE IF NOT EXISTS moves (
                       game_key TEXT NOT NULL,
                       seq INTEGER NOT NULL,
                       q1 INTEGER NOT NULL, r1 INTEGER NOT NULL,
                       q2 INTEGER NOT NULL, r2 INTEGER NOT NULL,
                       PRIMARY KEY (game_key, seq));");
        }

        /// <summary>Starts a game under this key. An existing game with the key is replaced (the "New game" reset).</summary>
        public void CreateGame(string key, string canonicalId)
        {
            lock (_lock)
            {
                using var tx = _conn.BeginTransaction();
                Exec("DELETE FROM moves WHERE game_key=$k", ("$k", key));
                Exec("DELETE FROM games WHERE key=$k", ("$k", key));
                Exec("INSERT INTO games (key, canonical_id, created_utc) VALUES ($k, $c, $t)",
                    ("$k", key), ("$c", canonicalId), ("$t", DateTime.UtcNow.ToString("o")));
                tx.Commit();
            }
        }

        public void AppendMove(string key, int q1, int r1, int q2, int r2)
        {
            lock (_lock)
                Exec(@"INSERT INTO moves (game_key, seq, q1, r1, q2, r2)
                       SELECT $k, COALESCE(MAX(seq), 0) + 1, $q1, $r1, $q2, $r2 FROM moves WHERE game_key=$k",
                    ("$k", key), ("$q1", q1), ("$r1", r1), ("$q2", q2), ("$r2", r2));
        }

        public void RemoveLastMove(string key)
        {
            lock (_lock)
                Exec("DELETE FROM moves WHERE game_key=$k AND seq=(SELECT MAX(seq) FROM moves WHERE game_key=$k)", ("$k", key));
        }

        public List<StoredGame> LoadAll()
        {
            lock (_lock)
            {
                var games = new Dictionary<string, StoredGame>();
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT key, canonical_id FROM games";
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) games[r.GetString(0)] = new StoredGame(r.GetString(0), r.GetString(1), new());
                }
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT game_key, q1, r1, q2, r2 FROM moves ORDER BY game_key, seq";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                        if (games.TryGetValue(r.GetString(0), out var g))
                            g.Moves.Add((r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4)));
                }
                return games.Values.ToList();
            }
        }

        private void Exec(string sql, params (string Name, object Value)[] args)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
            cmd.ExecuteNonQuery();
        }

        public void Dispose() => _conn.Dispose();
    }
}
