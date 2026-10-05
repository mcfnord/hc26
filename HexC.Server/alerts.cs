using HexC.Engine;

namespace HexC.Server
{
    /// <summary>
    /// Pushes short alerts to the operator's phone through ntfy.sh (PLAN.md 2026-10-05). The topic
    /// name is the only secret: it lives in HEXC_NTFY_TOPIC (from /etc/hexc/alerts.env), never in
    /// git. Without it, alerts are only logged. Every alert is also written to the journal, so
    /// `journalctl -u hexc | grep ALERT` is the full history.
    /// </summary>
    public static class Alerts
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly string? _topic = Environment.GetEnvironmentVariable("HEXC_NTFY_TOPIC");
        // The operator (HEXC_OPERATOR_ID, their Google user id) is not alerted about themselves.
        public static readonly string? OperatorId = Environment.GetEnvironmentVariable("HEXC_OPERATOR_ID");
        public const string SiteUrl = "https://johns.living";

        /// <summary>Tests replace this to capture alerts instead of sending them.</summary>
        public static Action<string, string, string?> Sink { get; set; } = Send;

        public static void Raise(string title, string message, string? clickUrl = null) => Sink(title, message, clickUrl);

        private static void Send(string title, string message, string? clickUrl)
        {
            Console.WriteLine($"ALERT {title}: {message} {clickUrl}");
            if (string.IsNullOrEmpty(_topic)) return;
            var req = new HttpRequestMessage(HttpMethod.Post, $"https://ntfy.sh/{_topic}") { Content = new StringContent(message) };
            req.Headers.Add("Title", title);
            if (clickUrl != null) req.Headers.Add("Click", clickUrl);
            // Fire and forget: a slow or failing ntfy.sh must never delay a move.
            _ = _http.SendAsync(req).ContinueWith(t =>
            {
                if (t.IsFaulted || !t.Result.IsSuccessStatusCode)
                    Console.Error.WriteLine($"ALERT send failed: {t.Exception?.GetBaseException().Message ?? t.Result.StatusCode.ToString()}");
            });
        }
    }

    /// <summary>
    /// Positions the operator asked to be told about when they happen in a real game, so a fix can
    /// be watched live. Checked after every accepted move. Each watch names the bug it guards.
    /// </summary>
    public static class Watches
    {
        /// <summary>Returns a description for each watch the game's last move triggered.</summary>
        public static List<string> Check(Game game)
        {
            var hits = new List<string>();
            var m = game.Timeline.LastOrDefault()?.LastMove;
            if (m == null) return hits;

            // 2026-10-05, fixed in 1ec585f: a piece leaving the portal by capture reincarnated its
            // own type onto the portal; the page then swapped the two pieces on every poll.
            var onOrigin = game.Board.AnyoneThere(new BoardLocation(m.FromQ, m.FromR));
            if (onOrigin != null && onOrigin.Color == m.Color && onOrigin.PieceType == m.Piece)
                hits.Add($"Twin on origin: {m.Color} {m.Piece} ({m.FromQ},{m.FromR})->({m.ToQ},{m.ToR}) left a {m.Piece} of its colour behind. Check the two don't swap back and forth.");

            return hits;
        }
    }
}
