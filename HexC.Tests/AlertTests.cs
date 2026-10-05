using System.Collections.Concurrent;
using System.Net.Http.Json;
using FluentAssertions;
using HexC.Engine;
using HexC.Server;
using Xunit;

namespace HexC.Tests;

/// <summary>Operator alerts (PLAN.md 2026-10-05): watched positions and people sitting down to play.</summary>
public class AlertTests : IClassFixture<AuthTests.FakeGoogleFactory>
{
    // Alerts.Sink is static and other tests run alongside, so filter captured alerts by a unique tag.
    private static readonly ConcurrentQueue<(string Title, string Message, string? Url)> Captured = new();
    static AlertTests() => Alerts.Sink = (t, m, u) => Captured.Enqueue((t, m, u));

    private readonly AuthTests.FakeGoogleFactory _factory;
    public AlertTests(AuthTests.FakeGoogleFactory factory) => _factory = factory;

    // The live game `main` (2026-10-05): a White Castle on the portal captures a Blue Castle at
    // (0,-3), and a White Castle reincarnates onto the portal it just left.
    [Fact]
    public void TwinOnOriginWatch_FiresWhenAPieceLeavesThePortalAndItsTwinReincarnatesThere()
    {
        var game = BoardBuilder.Create()
            .WithKing(ColorsEnum.Blue, -5, 3).WithKing(ColorsEnum.White, 5, -2).WithKing(ColorsEnum.Red, 2, 3)
            .WithCastle(ColorsEnum.White, 0, 0).WithCastle(ColorsEnum.Blue, 0, -3)
            .BuildGame(ColorsEnum.White);
        Watches.Check(game).Should().BeEmpty("no move yet");

        game.SubmitMove(0, 0, 0, -3);
        var portal = game.Board.AnyoneThere(new BoardLocation(0, 0));
        (portal?.Color, portal?.PieceType).Should().Be((ColorsEnum.White, PiecesEnum.Castle), BoardDiagnostics.Describe(game.Board));
        Watches.Check(game).Should().ContainSingle().Which.Should().StartWith("Twin on origin: White Castle (0,0)->(0,-3)");
    }

    [Fact]
    public void TwinOnOriginWatch_IgnoresAnOrdinaryMove()
    {
        var game = BoardBuilder.Create()
            .WithKing(ColorsEnum.Blue, -5, 3).WithKing(ColorsEnum.White, 5, -2).WithKing(ColorsEnum.Red, 2, 3)
            .WithCastle(ColorsEnum.White, 0, -1)
            .BuildGame(ColorsEnum.White);
        game.SubmitMove(0, -1, 0, -3);
        game.CurrentTurn.Should().Be(ColorsEnum.Red, game.StatusMessage);
        Watches.Check(game).Should().BeEmpty();
    }

    [Fact]
    public async Task SittingDownAtATable_AlertsTheOperator_WithTheName()
    {
        var name = "Zed" + Guid.NewGuid().ToString("N")[..6];
        var client = _factory.CreateClient();
        (await client.PostAsJsonAsync("/Auth/google", new { credential = $"user:{name}:{name}" })).EnsureSuccessStatusCode();
        try
        {
            (await client.PostAsync("/Lobby/enter", null)).EnsureSuccessStatusCode();
            (await client.PostAsync("/Lobby/enter", null)).EnsureSuccessStatusCode();
            Captured.Where(a => a.Title.Contains(name)).Should().ContainSingle("entering again is idempotent and must not alert twice")
                .Which.Url.Should().Be(Alerts.SiteUrl);
        }
        finally
        {
            await client.PostAsync("/Lobby/leave", null);   // leave no table waiting for other tests
        }
    }
}
