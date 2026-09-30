using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HexC.AI;
using HexC.Engine;
using HexC.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HexC.Tests;

/// <summary>
/// The zero-button landing flow (PLAN.md 2026-09-30) through the real pipeline with fake Google
/// sign-ins. The lobby is global (one waiting table), so each test leaves no table waiting.
/// </summary>
public class LobbyTests : IClassFixture<AuthTests.FakeGoogleFactory>
{
    private readonly AuthTests.FakeGoogleFactory _factory;
    public LobbyTests(AuthTests.FakeGoogleFactory factory) => _factory = factory;

    private async Task<HttpClient> SignedIn(string id, string name)
    {
        var client = _factory.CreateClient();
        (await client.PostAsJsonAsync("/Auth/google", new { credential = $"user:{id}:{name}" })).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<JObject> Json(HttpResponseMessage r) => JObject.Parse(await r.Content.ReadAsStringAsync());
    private static async Task<JObject> Enter(HttpClient c) => await Json(await c.PostAsync("/Lobby/enter", null));
    private static async Task<JObject> Status(HttpClient c, string gameId) => await Json(await c.GetAsync($"/Game/status?gameId={gameId}"));
    private static string[] Colors(JToken seats) => seats.Select(s => (string)s["color"]!).ToArray();

    [Fact]
    public async Task TableFlow_FirstWaits_SecondStartsCountdown_AiTakesThird_ThenSeatsAndTurnClockAreEnforced()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var ada = await SignedIn($"ada{tag}", "Ada");
        var bob = await SignedIn($"bob{tag}", "Bob");

        // 1. The first arrival waits alone, with no countdown.
        var a1 = await Enter(ada);
        ((bool)a1["waiting"]!).Should().BeTrue();
        ((string)a1["yourColor"]!).Should().Be("Blue");
        Colors(a1["seats"]!).Should().Equal("Blue");
        a1["countdownEndsUtc"]!.Type.Should().Be(JTokenType.Null);
        var gameId = (string)a1["gameId"]!;
        ((string)(await Enter(ada))["gameId"]!).Should().Be(gameId, "entering again is idempotent");

        // 2. The second arrival joins the same table and the countdown starts.
        var b1 = await Enter(bob);
        ((string)b1["gameId"]!).Should().Be(gameId);
        ((bool)b1["waiting"]!).Should().BeTrue();
        ((string)b1["yourColor"]!).Should().Be("White");
        b1["countdownEndsUtc"]!.Type.Should().NotBe(JTokenType.Null);

        // 3. The countdown runs out on the server clock: AI takes Red and the game starts.
        GameStore.Tick(DateTime.UtcNow + TimeSpan.FromMinutes(2));
        var a2 = await Enter(ada);
        ((bool)a2["waiting"]!).Should().BeFalse();
        Colors(a2["seats"]!).Should().Equal("Blue", "White", "Red");
        ((bool)a2["seats"]![2]!["isAi"]!).Should().BeTrue();

        var status = await Status(ada, gameId);
        ((string)status["turn"]!).Should().Be("Blue");
        ((string)status["yourColor"]!).Should().Be("Blue");
        status["aiColors"]!.Select(c => (string)c!).Should().Equal("Red");
        ((string)status["seats"]![1]!["name"]!).Should().Be("Bob");

        // 4. Only the person in the seat may move for it; AI endpoint refuses human seats.
        var game = GameStore.Get(gameId);
        var move = new BasicBot(ColorsEnum.Blue).PickMove(game.Board)!;
        var moveUrl = $"/Game/move?gameId={gameId}&q1={move.Q1}&r1={move.R1}&q2={move.Q2}&r2={move.R2}";
        (await bob.PostAsync(moveUrl, null)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "it is Ada's seat");
        (await _factory.CreateClient().PostAsync(moveUrl, null)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "anonymous visitors can't move at a table");
        (await ada.PostAsync(moveUrl, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        ((string)(await Status(ada, gameId))["turn"]!).Should().Be("White");
        (await ada.PostAsync($"/Game/ai-move?gameId={gameId}&forColor=White", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "Bob's seat is a person");
        (await ada.PostAsync($"/Game/undo?gameId={gameId}", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ada.PostAsync($"/Game/reset?gameId={gameId}", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 5. The turn clock: Bob away for ten minutes is fine; over an hour and the AI moves for him.
        GameStore.Tick(DateTime.UtcNow + TimeSpan.FromMinutes(10));
        ((string)(await Status(ada, gameId))["turn"]!).Should().Be("White");
        GameStore.Tick(DateTime.UtcNow + TimeSpan.FromHours(2));
        ((string)(await Status(ada, gameId))["turn"]!).Should().Be("Red");

        // 6. Red is an AI seat, so a page may drive it as usual.
        (await bob.PostAsync($"/Game/ai-move?gameId={gameId}&forColor=Red", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        ((string)(await Status(ada, gameId))["turn"]!).Should().Be("Blue");

        // 7. Both are "in a game" now: entering brings them back to it, not to a new table.
        ((string)(await Enter(bob))["gameId"]!).Should().Be(gameId);
    }

    [Fact]
    public async Task ThreeHumans_StartAtOnce_AndLeavingAnEmptyTableRemovesIt()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var c = await SignedIn($"cy{tag}", "Cy");
        var d = await SignedIn($"di{tag}", "Di");
        var e = await SignedIn($"ed{tag}", "Ed");

        var c1 = await Enter(c);
        await Enter(d);
        var e1 = await Enter(e);
        ((string)e1["gameId"]!).Should().Be((string)c1["gameId"]!);
        ((bool)e1["waiting"]!).Should().BeFalse("three humans start at once");
        e1["seats"]!.Select(s => (bool)s["isAi"]!).Should().OnlyContain(ai => !ai);
        ((string)e1["yourColor"]!).Should().Be("Red");

        // A lone waiter who signs out leaves an empty table, which disappears.
        var f = await SignedIn($"fy{tag}", "Fy");
        var f1 = await Enter(f);
        ((bool)f1["waiting"]!).Should().BeTrue();
        ((bool)(await Json(await f.PostAsync("/Lobby/leave", null)))["left"]!).Should().BeTrue();
        (await f.GetAsync($"/Game/status?gameId={f1["gameId"]}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var f2 = await Enter(f);
        ((string)f2["gameId"]!).Should().NotBe((string)f1["gameId"]!, "the old table is gone");
        (await f.PostAsync("/Lobby/leave", null)).EnsureSuccessStatusCode();   // leave nothing waiting

        (await _factory.CreateClient().PostAsync("/Lobby/enter", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
