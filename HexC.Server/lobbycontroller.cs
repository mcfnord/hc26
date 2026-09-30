using System.Security.Claims;
using HexC.Engine;
using Microsoft.AspNetCore.Mvc;

namespace HexC.Server.Controllers
{
    /// <summary>
    /// The zero-button landing flow (PLAN.md 2026-09-30): a signed-in page asks where it belongs
    /// and is seated. Polled while waiting.
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class LobbyController : ControllerBase
    {
        private string? UserId => User.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;

        [HttpPost("enter")]
        public IActionResult Enter()
        {
            var uid = UserId;
            if (uid == null) return Unauthorized("Sign in first.");
            var name = User.FindFirstValue(ClaimTypes.Name) ?? "Player";
            var meta = GameStore.Enter(uid, name, DateTime.UtcNow);
            return Ok(View(meta, uid));
        }

        [HttpPost("leave")]
        public IActionResult Leave()
        {
            var uid = UserId;
            if (uid == null) return Unauthorized("Sign in first.");
            return Ok(new { left = GameStore.Leave(uid) });
        }

        public static object View(GameMeta meta, string? uid) => new
        {
            GameId = meta.Key,
            Waiting = meta.Waiting,
            CountdownEndsUtc = meta.CountdownEndsUtc,
            Seats = SeatsView(meta),
            YourColor = uid == null ? null : meta.SeatOfUser(uid)?.Color.ToString()
        };

        public static IEnumerable<object> SeatsView(GameMeta meta) => meta.Seats
            .OrderBy(s => s.Color == ColorsEnum.Blue ? 0 : s.Color == ColorsEnum.White ? 1 : 2)
            .Select(s => new { Color = s.Color.ToString(), Name = s.IsAi ? "AI" : s.UserName, IsAi = s.IsAi, OnAi = !s.IsAi && meta.IsOnAi(s.Color) });
    }
}
