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
    }
}