using System.Security.Claims;
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

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Subject),
                new(ClaimTypes.Email, user.Email),
                new(ClaimTypes.Name, user.Name),
            };
            if (user.Picture != null) claims.Add(new("picture", user.Picture));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
                new AuthenticationProperties { IsPersistent = true });
            return Ok(new { signedIn = true, name = user.Name, picture = user.Picture });
        }

        [HttpGet("me")]
        public IActionResult Me([FromServices] IConfiguration config)
        {
            var clientId = config["Google:ClientId"];
            if (User.Identity?.IsAuthenticated != true) return Ok(new { signedIn = false, clientId });
            return Ok(new
            {
                signedIn = true,
                clientId,
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
