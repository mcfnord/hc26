using System.Security.Claims;
using System.Text.RegularExpressions;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace HexC.Server.Controllers
{
    // Checks a Google ID token and returns the signed-in person, or null if the token is bad.
    // An interface so the API tests can sign in without talking to Google.
    public interface IGoogleTokenValidator
    {
        Task<GoogleUser?> ValidateAsync(string credential);
    }

    public record GoogleUser(string Subject, string Email, string Name, string? Picture);

    public class GoogleTokenValidator : IGoogleTokenValidator
    {
        private readonly string _clientId;
        public GoogleTokenValidator(IConfiguration config) =>
            _clientId = config["Google:ClientId"] ?? throw new InvalidOperationException("Google:ClientId is not configured.");

        public async Task<GoogleUser?> ValidateAsync(string credential)
        {
            try
            {
                var p = await GoogleJsonWebSignature.ValidateAsync(credential,
                    new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { _clientId } });
                return new GoogleUser(p.Subject, p.Email, p.Name ?? p.Email, p.Picture);
            }
            catch (InvalidJwtException)
            {
                return null;
            }
        }
    }

    // Google sign-in: the page gets an ID token from Google, posts it here, and gets our own
    // session cookie back. No passwords are stored. See PLAN.md (Auth).
    [ApiController]
    [Route("[controller]")]
    public class AuthController : ControllerBase
    {
        public record GoogleSignIn(string Credential);

        [HttpPost("google")]
        public async Task<IActionResult> SignInWithGoogle([FromBody] GoogleSignIn body, [FromServices] IGoogleTokenValidator validator)
        {
            if (string.IsNullOrWhiteSpace(body?.Credential)) return BadRequest("Missing credential.");
            var user = await validator.ValidateAsync(body.Credential);
            if (user == null) return Unauthorized("Google sign-in could not be verified.");
            GameStore.RecordUser(user.Subject, user.Email, user.Name, user.Picture);
            return await SignIn(user.Subject, user.Email, user.Name, user.Picture);
        }

        // The test box only (PLAN.md Vision 2, step 0.2): sign in by typing a name, no Google.
        // deploy.sh sets HEXC_TESTBOX=1 only on a machine that has /etc/hexc/testbox.env, so
        // johns.living never offers this. The same name is always the same player.
        public record TestSignIn(string Name);

        public static bool IsTestBox(IConfiguration config) => config["HEXC_TESTBOX"] == "1";

        [HttpPost("test")]
        public async Task<IActionResult> SignInForTesting([FromBody] TestSignIn body, [FromServices] IConfiguration config)
        {
            if (!IsTestBox(config)) return NotFound();
            var name = (body?.Name ?? "").Trim();
            if (!Regex.IsMatch(name, @"^[A-Za-z][A-Za-z0-9 ]{0,19}$"))
                return BadRequest("Use up to 20 letters, digits or spaces, starting with a letter.");
            var id = "test:" + name.ToLowerInvariant();
            GameStore.RecordUser(id, null, name, null);
            return await SignIn(id, null, name, null);
        }

        private async Task<IActionResult> SignIn(string id, string? email, string name, string? picture)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, id),
                new(ClaimTypes.Name, name),
            };
            if (email != null) claims.Add(new(ClaimTypes.Email, email));
            if (picture != null) claims.Add(new("picture", picture));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
                new AuthenticationProperties { IsPersistent = true });
            return Ok(new { signedIn = true, name, picture });
        }

        [HttpGet("me")]
        public IActionResult Me([FromServices] IConfiguration config)
        {
            var clientId = config["Google:ClientId"];
            var testBox = IsTestBox(config);
            if (User.Identity?.IsAuthenticated != true) return Ok(new { signedIn = false, clientId, testBox });
            return Ok(new
            {
                signedIn = true,
                clientId,
                testBox,
                name = User.FindFirstValue(ClaimTypes.Name),
                picture = User.FindFirstValue("picture"),
            });
        }

        [HttpPost("signout")]
        public async Task<IActionResult> SignOutUser()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok(new { signedIn = false });
        }
    }
}
