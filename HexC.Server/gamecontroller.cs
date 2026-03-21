using Microsoft.AspNetCore.Mvc;
using HexC.Engine;

namespace HexC.Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class GameController : ControllerBase
    {
        [HttpPost("create")]
        public IActionResult CreateGame(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                return BadRequest("Game ID cannot be empty.");

            if (GameStore.Exists(gameId)) 
                return Conflict($"Game {gameId} already exists.");
            
            GameStore.Create(gameId);
            return Ok($"Game {gameId} created. White to move.");
        }

        [HttpGet("status")]
        public IActionResult GetStatus(string gameId)
        {
            var game = GameStore.Get(gameId);
            if (game == null) return NotFound("Game not found");

            return Ok(new { 
                Turn = game.CurrentTurn.ToString(),
                State = game.State.ToString(),
                Message = game.StatusMessage
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
                R = p.Location.R
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
            var threats = simBoard.GetThreatsToSquare(target, game.CurrentTurn);

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

            // Capture state before move to see if it succeeds
            var turnBefore = game.CurrentTurn;
            
            // Attempt the move directly using the Engine
            game.SubmitMove(q1, r1, q2, r2);

            // If the turn changed OR the game ended, the move was successful
            bool success = (game.CurrentTurn != turnBefore) || (game.State == GameStateEnum.Finished);

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

        [HttpPost("undo")]
        public IActionResult UndoMove(string gameId)
        {
            var game = GameStore.Get(gameId);
            if (game == null) return NotFound("Game not found");

            if (game.TakeBack())
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