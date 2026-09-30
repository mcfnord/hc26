using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using HexC.Engine;
using HexC.AI;

namespace HexC.Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class GameController : ControllerBase
    {
        private string? UserId => User.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;

        private static string SeatName(GameMeta meta, ColorsEnum color)
        {
            var seat = meta.SeatOf(color);
            return seat == null ? color.ToString() : $"{color} ({(seat.IsAi ? "AI" : seat.UserName)})";
        }

        [HttpPost("create")]
        public IActionResult CreateGame(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                return BadRequest("Game ID cannot be empty.");

            if (!gameId.All(char.IsLetter))
                return BadRequest("Game ID must contain only letters.");

            if (GameStore.Exists(gameId)) 
                return Conflict($"Game {gameId} already exists.");
            
            var created = GameStore.Create(gameId);
            return Ok($"Game {gameId} created. {created.CurrentTurn} to move.");
        }

        /// <summary>Replace an existing game with a fresh one under the same ID (the "New game" button).</summary>
        [HttpPost("reset")]
        public IActionResult ResetGame(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId) || !gameId.All(char.IsLetter))
                return BadRequest("Game ID must contain only letters.");
            if (GameStore.Meta(gameId)?.IsTable == true)
                return StatusCode(403, new { Success = false, Message = "A table game cannot be reset." });
            var canonical = GameStore.GetCanonicalId(gameId);
            var game = GameStore.Create(canonical);
            return Ok(new { Success = true, NewTurn = game.CurrentTurn.ToString(), Message = $"New game. {game.CurrentTurn} to move." });
        }

        [HttpGet("canonicalId")]
        public IActionResult GetCanonicalId(string gameId)
        {
            if (!GameStore.Exists(gameId)) return NotFound("Game not found");
            return Ok(GameStore.GetCanonicalId(gameId));
        }

        [HttpGet("status")]
        public IActionResult GetStatus(string gameId, int? idle = null)
        {
            var game = GameStore.Get(gameId);
            if (game == null) return NotFound("Game not found");

            var meta = GameStore.Meta(gameId);
            var isTable = meta?.IsTable == true;
            var uid = UserId;
            // idle = seconds since the person last touched the page (absence rule, PLAN.md 2026-09-30).
            if (uid != null && idle != null && idle >= 0)
                GameStore.Touch(uid, DateTime.UtcNow - TimeSpan.FromSeconds(Math.Min(idle.Value, 86400)));
            return Ok(new {
                Turn = game.CurrentTurn.ToString(),
                State = game.State.ToString(),
                Message = game.StatusMessage,
                CheckStatuses = game.GetCheckStatuses(),
                // Table games: who sits where, and which seats the page should drive the AI for.
                // Anonymous games: no seats; the browser plays Blue and drives White and Red.
                Seats = isTable ? LobbyController.SeatsView(meta!) : null,
                Waiting = meta?.Waiting ?? false,
                // When the AI will move for the person on turn; null if it's an AI seat or the AI already plays it.
                AiStepsInUtc = isTable ? GameStore.AiStepsInUtc(meta!, game) : null,
                YourColor = isTable && uid != null ? meta!.SeatOfUser(uid)?.Color.ToString() : null,
                AiColors = isTable
                    ? meta!.Seats.Where(s => s.IsAi).Select(s => s.Color.ToString()).ToArray()
                    : new[] { "White", "Red" },
                // Last two moves, most recent first, so the UI can draw traces (from -> to).
                RecentMoves = game.Timeline
                    .Where(snap => snap.LastMove != null)
                    .Reverse().Take(2)
                    .Select(snap => new {
                        Color = snap.LastMove!.Color.ToString(),
                        Piece = snap.LastMove.Piece.ToString(),
                        FromQ = snap.LastMove.FromQ, FromR = snap.LastMove.FromR,
                        ToQ = snap.LastMove.ToQ, ToR = snap.LastMove.ToR
                    })
            });
        }

        [HttpGet("board")]
        public IActionResult GetBoard(string gameId)
        {
            var game = GameStore.Get(gameId);
            if (game == null) return NotFound("Game not found");

            // Transform the complex Board object into a simple list of pieces for the web client
            var pieces = game.Board.PlacedPieces.Select(p => new {
                Piece = p.PieceType.ToString(),
                Color = p.Color.ToString(),
                Q = p.Location.Q,
                R = p.Location.R,
                IsMob = game.Board.IsInMob(p)
            });

            return Ok(pieces);
        }

        [HttpGet("validMoves")]
        public IActionResult GetValidMoves(string gameId, int q, int r)
        {
            var game = GameStore.Get(gameId);
            if (game == null) return NotFound("Game not found");

            var moves = game.GetValidMoves(q, r).Select(loc => new { Q = loc.Q, R = loc.R });
            return Ok(moves);
        }

        /// <summary>
        /// Returns enemy pieces that threaten a given square, with attack paths.
        /// Used by the UI to visualize WHY a move into check is illegal.
        ///
        /// fromQ/fromR: origin of the piece that tried to move. The endpoint
        /// simulates the board without it so the piece doesn't block its own
        /// threat line.
        /// </summary>
        [HttpGet("threats")]
        public IActionResult GetThreats(
            string gameId, int q, int r, int fromQ, int fromR)
        {
            var game = GameStore.Get(gameId);
            if (game == null) return NotFound("Game not found");

            // Simulate: remove moving piece so it can't block slides
            var simBoard = new Board(game.Board);
            var movingPiece = simBoard.AnyoneThere(new BoardLocation(fromQ, fromR));
            if (movingPiece != null) simBoard.Remove(movingPiece);

            var target = new BoardLocation(q, r);
            var friendlyColor = movingPiece?.Color ?? game.CurrentTurn;
            var threats = simBoard.GetThreatsToSquare(target, friendlyColor);

            var result = threats.Select(t => new {
                Attacker = new {
                    Piece = t.Attacker.PieceType.ToString(),
                    Color = t.Attacker.Color.ToString(),
                    Q = t.Attacker.Location.Q,
                    R = t.Attacker.Location.R
                },
                Path = t.Path.Select(p => new { Q = p.Q, R = p.R })
            });

            return Ok(result);
        }

        [HttpPost("move")]
        public IActionResult SubmitMove(string gameId, int q1, int r1, int q2, int r2)
        {
            var game = GameStore.Get(gameId);
            if (game == null) return NotFound("Game not found");

            var meta = GameStore.Meta(gameId);
            if (meta?.IsTable == true)
            {
                if (meta.Waiting) return BadRequest(new { Success = false, Message = "Waiting for opponents." });
                var seat = meta.SeatOf(game.CurrentTurn);
                if (seat == null || seat.IsAi || seat.UserId != UserId)
                    return StatusCode(403, new { Success = false, Message = $"It is {SeatName(meta, game.CurrentTurn)}'s turn." });
                GameStore.HumanMoved(meta, seat.Color, seat.UserId!, DateTime.UtcNow);
            }

            // The store infers success from the state change and persists accepted moves.
            bool success = GameStore.TrySubmitMove(gameId, game, q1, r1, q2, r2);

            if (success)
                return Ok(new { Success = true, NewTurn = game.CurrentTurn.ToString(), Message = game.StatusMessage });
            else
                return BadRequest(new { Success = false, Message = game.StatusMessage });
        }

        [HttpGet("export")]
        public IActionResult ExportGame(string gameId)
        {
            var game = GameStore.Get(gameId);
            if (game == null) return NotFound("Game not found");

            var state = new
            {
                GameId = gameId,
                CurrentTurn = game.CurrentTurn.ToString(),
                GameState = game.State.ToString(),
                StatusMessage = game.StatusMessage,
                Pieces = game.Board.PlacedPieces.Select(p => new
                {
                    PieceType = p.PieceType.ToString(),
                    Color = p.Color.ToString(),
                    Q = p.Location.Q,
                    R = p.Location.R
                })
            };

            return Ok(state);
        }

        [HttpGet("sidelined")]
        public IActionResult GetSidelined(string gameId)
        {
            var game = GameStore.Get(gameId);
            if (game == null) return NotFound("Game not found");

            var sidelined = game.Board.SidelinedPieces.Select(p => new {
                Piece = p.PieceType.ToString(),
                Color = p.Color.ToString()
            });

            return Ok(sidelined);
        }

        [HttpGet("timeline")]
        public IActionResult GetTimeline(string gameId)
        {
            var game = GameStore.Get(gameId);
            if (game == null) return NotFound("Game not found");

            var history = game.Timeline.Select((snap, i) => new {
                FrameIndex = i,
                Turn = snap.CurrentTurn.ToString(),
                State = snap.State.ToString(),
                StatusMessage = snap.StatusMessage,
                MoveHistory = snap.MoveHistory,
                Move = snap.LastMove == null ? null : new {
                    Color = snap.LastMove.Color.ToString(),
                    Piece = snap.LastMove.Piece.ToString(),
                    FromQ = snap.LastMove.FromQ,
                    FromR = snap.LastMove.FromR,
                    ToQ = snap.LastMove.ToQ,
                    ToR = snap.LastMove.ToR,
                    CapturedColor = snap.LastMove.CapturedColor?.ToString(),
                    CapturedPiece = snap.LastMove.CapturedPiece?.ToString(),
                    ReincarnatedPiece = snap.LastMove.ReincarnatedPiece?.ToString()
                },
                Pieces = snap.Board.PlacedPieces.Select(p => new {
                    Piece = p.PieceType.ToString(),
                    Color = p.Color.ToString(),
                    Q = p.Location.Q,
                    R = p.Location.R,
                    IsMob = snap.Board.IsInMob(p)
                }).ToList()
            }).ToList();

            return Ok(history);
        }

        [HttpPost("ai-move")]
        public IActionResult MakeAiMove(string gameId, string forColor = null)
        {
            var game = GameStore.Get(gameId);
            if (game == null) return NotFound("Game not found");
            // Serialize per game: two browser tabs polling the same game must not both move.
            lock (game)
            {
                if (game.State == GameStateEnum.Finished) return BadRequest("Game over");
                var meta = GameStore.Meta(gameId);
                if (meta?.IsTable == true)
                {
                    if (meta.Waiting) return BadRequest(new { Success = false, Message = "Waiting for opponents." });
                    var seat = meta.SeatOf(game.CurrentTurn);
                    if (seat == null || !seat.IsAi)
                        return StatusCode(403, new { Success = false, Message = $"{SeatName(meta, game.CurrentTurn)} is a person, not an AI." });
                }

                // Optional guard: the caller says whose turn it believes it is. If the game has
                // moved on (another tab already made this AI move), refuse rather than move for
                // the wrong player.
                if (!string.IsNullOrEmpty(forColor) && !string.Equals(forColor, game.CurrentTurn.ToString(), StringComparison.OrdinalIgnoreCase))
                    return Conflict(new { Success = false, Message = $"It is {game.CurrentTurn}'s turn, not {forColor}'s." });

                var bot = new BasicBot(game.CurrentTurn);
                var move = bot.PickMove(game.Board);

                if (move != null)
                {
                    GameStore.TrySubmitMove(gameId, game, move.Q1, move.R1, move.Q2, move.R2);
                    return Ok(new { Success = true, Message = game.StatusMessage });
                }

                return BadRequest("No moves available for AI");
            }
        }

        [HttpPost("undo")]
        public IActionResult UndoMove(string gameId)
        {
            var game = GameStore.Get(gameId);
            if (game == null) return NotFound("Game not found");

            if (GameStore.Meta(gameId)?.IsTable == true)
                return StatusCode(403, new { Success = false, Message = "Undo is not available in a table game." });
            if (GameStore.TryTakeBack(gameId, game))
            {
                return Ok(new { Success = true, NewTurn = game.CurrentTurn.ToString(), Message = game.StatusMessage ?? "Move reversed." });
            }
            else
            {
                return BadRequest("Already at the start of the game.");
            }
        }
    }
}