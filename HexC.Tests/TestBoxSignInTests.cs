using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HexC.Tests;

/// <summary>
/// Sign-in by typed name on the test box (PLAN.md Vision 2, step 0.2). Only a server started with
/// HEXC_TESTBOX=1 offers it; deploy.sh sets that only on the test box, never on johns.living.
/// </summary>
public class TestBoxSignInTests : IClassFixture<TestBoxSignInTests.TestBoxFactory>, IClassFixture<AuthTests.FakeGoogleFactory>
{
    public class TestBoxFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?> { ["HEXC_TESTBOX"] = "1" }));
        }
    }

    private readonly TestBoxFactory _testBox;
    private readonly AuthTests.FakeGoogleFactory _normal;
    public TestBoxSignInTests(TestBoxFactory testBox, AuthTests.FakeGoogleFactory normal) { _testBox = testBox; _normal = normal; }

    private static async Task<JObject> Json(HttpResponseMessage r) => JObject.Parse(await r.Content.ReadAsStringAsync());
    // GameStore is static and shared with other tests, so every player gets a unique name.
    private static string UniqueName(string first) => first + new string(Guid.NewGuid().ToString("N")[..8].Select(c => (char)('a' + c % 26)).ToArray());

    [Fact]
    public async Task OffTheTestBox_TypedSignInIsRefused_AndMeSaysSo()
    {
        var client = _normal.CreateClient();
        var res = await client.PostAsJsonAsync("/Auth/test", new { name = "Mallory" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);

        var me = await Json(await client.GetAsync("/Auth/me"));
        Assert.False((bool)me["signedIn"]!);
        Assert.False((bool)me["testBox"]!);
    }

    [Fact]
    public async Task OnTheTestBox_TypingANameSignsYouIn()
    {
        var client = _testBox.CreateClient();
        Assert.True((bool)(await Json(await client.GetAsync("/Auth/me")))["testBox"]!);

        var name = UniqueName("Alice");
        var res = await client.PostAsJsonAsync("/Auth/test", new { name = $"  {name} " });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var me = await Json(await client.GetAsync("/Auth/me"));
        Assert.True((bool)me["signedIn"]!);
        Assert.Equal(name, (string)me["name"]!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("9lives")]
    [InlineData("<script>")]
    [InlineData("Averyveryverylongname1")]
    public async Task OnTheTestBox_ABadNameIsTurnedAway(string name)
    {
        var client = _testBox.CreateClient();
        var res = await client.PostAsJsonAsync("/Auth/test", new { name });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.False((bool)(await Json(await client.GetAsync("/Auth/me")))["signedIn"]!);
    }

    [Fact]
    public async Task OnTheTestBox_TheSameNameIsTheSamePlayer_SoYouLandBackInYourSeat()
    {
        var name = UniqueName("Bea");
        var first = _testBox.CreateClient();
        (await first.PostAsJsonAsync("/Auth/test", new { name })).EnsureSuccessStatusCode();
        var seated = await Json(await first.PostAsync("/Lobby/enter", null));
        try
        {
            // Another browser, the name typed in another case: the same player, the same seat.
            var second = _testBox.CreateClient();
            (await second.PostAsJsonAsync("/Auth/test", new { name = name.ToUpperInvariant() })).EnsureSuccessStatusCode();
            var again = await Json(await second.PostAsync("/Lobby/enter", null));
            Assert.Equal((string)seated["gameId"]!, (string)again["gameId"]!);
            Assert.Equal((string)seated["yourColor"]!, (string)again["yourColor"]!);
        }
        finally
        {
            await first.PostAsync("/Lobby/leave", null);   // leave no table waiting for other tests
        }
    }

    [Fact]
    public async Task OnTheTestBox_TwoNamesSitDownAtTheSameTable()
    {
        var alice = _testBox.CreateClient();
        var bob = _testBox.CreateClient();
        var aliceName = UniqueName("Alice");
        var bobName = UniqueName("Bob");
        (await alice.PostAsJsonAsync("/Auth/test", new { name = aliceName })).EnsureSuccessStatusCode();
        (await bob.PostAsJsonAsync("/Auth/test", new { name = bobName })).EnsureSuccessStatusCode();
        try
        {
            var a = await Json(await alice.PostAsync("/Lobby/enter", null));
            var b = await Json(await bob.PostAsync("/Lobby/enter", null));
            Assert.Equal((string)a["gameId"]!, (string)b["gameId"]!);
            Assert.NotEqual((string)a["yourColor"]!, (string)b["yourColor"]!);
            var names = ((JArray)b["seats"]!).Select(s => (string)s["name"]!).ToList();
            Assert.Contains(aliceName, names);
            Assert.Contains(bobName, names);
        }
        finally
        {
            await alice.PostAsync("/Lobby/leave", null);
            await bob.PostAsync("/Lobby/leave", null);
        }
    }
}
