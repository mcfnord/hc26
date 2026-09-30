using System.Globalization;
using Microsoft.Data.Sqlite;

namespace HexC.Server
{
    /// <summary>
    /// SQLite storage for games as move lists (PLAN.md: Persistence). A game is its ID plus the
    /// ordered coordinates of every accepted SubmitMove; replaying them on a fresh Game rebuilds
    /// the exact state, undo stack included. Tables (PLAN.md 2026-09-30 landing flow) also store
    /// their seats and clocks. Deploys back this file up and reverts restore it.
    /// </summary>
    public sealed class GameRepository : IDisposable
    {
        public record StoredSeat(string Color, string? UserId, string? UserName);
        public record StoredGame(string Key, string CanonicalId, List<(int Q1, int R1, int Q2, int R2)> Moves,
            bool IsTable, DateTime? StartedUtc, DateTime? CountdownEndsUtc, DateTime? LastMoveUtc, List<StoredSeat> Seats);

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
                       PRIMARY KEY (game_key, seq));
                   CREATE TABLE IF NOT EXISTS seats (
                       game_key TEXT NOT NULL,
                       color TEXT NOT NULL,
                       user_id TEXT,
                       user_name TEXT,
                       PRIMARY KEY (game_key, color));
                   CREATE TABLE IF NOT EXISTS users (
                       id TEXT PRIMARY KEY,
                       email TEXT,
                       name TEXT,
                       picture TEXT,
                       first_seen_utc TEXT NOT NULL,
                       last_seen_utc TEXT NOT NULL);");
            // Columns added after the first release (2026-09-30); older databases gain them here.
            AddColumnIfMissing("games", "is_table", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing("games", "started_utc", "TEXT");
            AddColumnIfMissing("games", "countdown_ends_utc", "TEXT");
            AddColumnIfMissing("games", "last_move_utc", "TEXT");
        }

        /// <summary>Starts a game under this key. An existing game with the key is replaced (the "New game" reset).</summary>
        public void CreateGame(string key, string canonicalId, bool isTable = false)
        {
            lock (_lock)
            {
                using var tx = _conn.BeginTransaction();
                DeleteGameRows(key);
                Exec("INSERT INTO games (key, canonical_id, created_utc, is_table) VALUES ($k, $c, $t, $table)",
                    ("$k", key), ("$c", canonicalId), ("$t", Iso(DateTime.UtcNow)!), ("$table", isTable ? 1 : 0));
                tx.Commit();
            }
        }

        public void DeleteGame(string key)
        {
            lock (_lock)
            {
                using var tx = _conn.BeginTransaction();
                DeleteGameRows(key);
                tx.Commit();
            }
        }

        private void DeleteGameRows(string key)
        {
            Exec("DELETE FROM moves WHERE game_key=$k", ("$k", key));
            Exec("DELETE FROM seats WHERE game_key=$k", ("$k", key));
            Exec("DELETE FROM games WHERE key=$k", ("$k", key));
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

        public void SaveTableState(string key, DateTime? startedUtc, DateTime? countdownEndsUtc, DateTime? lastMoveUtc)
        {
            lock (_lock)
                Exec("UPDATE games SET started_utc=$s, countdown_ends_utc=$c, last_move_utc=$m WHERE key=$k",
                    ("$k", key), ("$s", Iso(startedUtc)), ("$c", Iso(countdownEndsUtc)), ("$m", Iso(lastMoveUtc)));
        }

        public void SetSeat(string key, string color, string? userId, string? userName)
        {
            lock (_lock)
                Exec("INSERT OR REPLACE INTO seats (game_key, color, user_id, user_name) VALUES ($k, $c, $u, $n)",
                    ("$k", key), ("$c", color), ("$u", userId), ("$n", userName));
        }

        public void ClearSeat(string key, string color)
        {
            lock (_lock)
                Exec("DELETE FROM seats WHERE game_key=$k AND color=$c", ("$k", key), ("$c", color));
        }

        public void UpsertUser(string id, string? email, string? name, string? picture)
        {
            lock (_lock)
                Exec(@"INSERT INTO users (id, email, name, picture, first_seen_utc, last_seen_utc) VALUES ($id, $e, $n, $p, $t, $t)
                       ON CONFLICT(id) DO UPDATE SET email=$e, name=$n, picture=$p, last_seen_utc=$t",
                    ("$id", id), ("$e", email), ("$n", name), ("$p", picture), ("$t", Iso(DateTime.UtcNow)!));
        }

        public List<StoredGame> LoadAll()
        {
            lock (_lock)
            {
                var games = new Dictionary<string, StoredGame>();
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT key, canonical_id, is_table, started_utc, countdown_ends_utc, last_move_utc FROM games";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                        games[r.GetString(0)] = new StoredGame(r.GetString(0), r.GetString(1), new(),
                            r.GetInt32(2) != 0, ReadDate(r, 3), ReadDate(r, 4), ReadDate(r, 5), new());
                }
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT game_key, q1, r1, q2, r2 FROM moves ORDER BY game_key, seq";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                        if (games.TryGetValue(r.GetString(0), out var g))
                            g.Moves.Add((r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4)));
                }
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT game_key, color, user_id, user_name FROM seats";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                        if (games.TryGetValue(r.GetString(0), out var g))
                            g.Seats.Add(new StoredSeat(r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3)));
                }
                return games.Values.ToList();
            }
        }

        private void AddColumnIfMissing(string table, string column, string definition)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info({table})";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
            }
            Exec($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
        }

        private static string? Iso(DateTime? d) => d?.ToUniversalTime().ToString("o");

        private static DateTime? ReadDate(SqliteDataReader r, int i) =>
            r.IsDBNull(i) ? null : DateTime.Parse(r.GetString(i), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        private void Exec(string sql, params (string Name, object? Value)[] args)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        public void Dispose() => _conn.Dispose();
    }
}
