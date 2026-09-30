using System.Net;
using System.Net.Http.Json;
using HexC.Server.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HexC.Tests;

/// <summary>
/// Google sign-in through the real pipeline, with Google's token check replaced by a fake:
/// the credential "good-token" is a valid user, anything else is rejected.
/// </summary>
public class AuthTests : IClassFixture<AuthTests.FakeGoogleFactory>
{
    public class FakeGoogleValidator : IGoogleTokenValidator
    {
        public Task<GoogleUser?> ValidateAsync(string credential) =>
            Task.FromResult(credential == "good-token"
                ? new GoogleUser("google-sub-123", "ada@example.com", "Ada Lovelace", "https://example.com/ada.png")
                : null);
    }

    public class FakeGoogleFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(s => s.AddSingleton<IGoogleTokenValidator, FakeGoogleValidator>());
        }
    }

    private readonly FakeGoogleFactory _factory;
    public AuthTests(FakeGoogleFactory factory) => _factory = factory;

    private static async Task<JObject> Json(HttpResponseMessage r) => JObject.Parse(await r.Content.ReadAsStringAsync());

    [Fact]
    public async Task Me_NotSignedIn_ReportsSignedOutAndTheClientId()
    {
        var client = _factory.CreateClient();
        var me = await Json(await client.GetAsync("/Auth/me"));
        Assert.False((bool)me["signedIn"]!);
        Assert.EndsWith(".apps.googleusercontent.com", (string)me["clientId"]!);
    }

    [Fact]
    public async Task SignIn_WithValidToken_SetsSessionAndMeShowsName()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/Auth/google", new { credential = "good-token" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains(res.Headers.GetValues("Set-Cookie"), c => c.StartsWith("hexc_session=") && c.Contains("httponly"));

        var me = await Json(await client.GetAsync("/Auth/me"));
        Assert.True((bool)me["signedIn"]!);
        Assert.Equal("Ada Lovelace", (string)me["name"]!);
    }

    [Fact]
    public async Task SignIn_WithBadToken_Returns401AndStaysSignedOut()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/Auth/google", new { credential = "forged" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

        var me = await Json(await client.GetAsync("/Auth/me"));
        Assert.False((bool)me["signedIn"]!);
    }

    [Fact]
    public async Task SignOut_ClearsTheSession()
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/Auth/google", new { credential = "good-token" });
        await client.PostAsync("/Auth/signout", null);

        var me = await Json(await client.GetAsync("/Auth/me"));
        Assert.False((bool)me["signedIn"]!);
    }
}
