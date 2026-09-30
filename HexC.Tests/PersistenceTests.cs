using FluentAssertions;
using HexC.AI;
using HexC.Engine;
using HexC.Server;
using Xunit;

namespace HexC.Tests;

/// <summary>
/// Games are stored as move lists and rebuilt by replay (GameStore.Rebuild). These tests drive a
/// temp database directly rather than the static GameStore, which other test classes share.
/// </summary>
public class PersistenceTests
{
    private static string TempDb() => Path.Combine(Path.GetTempPath(), $"hexc-test-{Guid.NewGuid():N}.db");

    private static void DeleteDb(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(path + suffix)) File.Delete(path + suffix);
    }

    // Plays n bot moves, recording each accepted move the way GameStore.TrySubmitMove does.
    private static List<(int Q1, int R1, int Q2, int R2)> PlayBotMoves(Game game, GameRepository repo, string key, int n)
    {
        var played = new List<(int, int, int, int)>();
        for (int i = 0; i < n; i++)
        {
            var move = new BasicBot(game.CurrentTurn).PickMove(game.Board);
            move.Should().NotBeNull();
            var turnBefore = game.CurrentTurn;
            game.SubmitMove(move!.Q1, move.R1, move.Q2, move.R2);
            game.CurrentTurn.Should().NotBe(turnBefore, "the bot only picks legal moves");
            repo.AppendMove(key, move.Q1, move.R1, move.Q2, move.R2);
            played.Add((move.Q1, move.R1, move.Q2, move.R2));
        }
        return played;
    }

    [Fact]
    public void ReloadedGame_ReplaysToTheSameState_IncludingUndoStack()
    {
        var path = TempDb();
        try
        {
            var live = new Game();
            List<(int Q1, int R1, int Q2, int R2)> expectedMoves;
            using (var repo = new GameRepository(path))
            {
                repo.CreateGame("alpha", "Alpha");
                expectedMoves = PlayBotMoves(live, repo, "alpha", 6);

                // Undo one move (as the undo endpoint does), then play one more.
                live.TakeBack().Should().BeTrue();
                repo.RemoveLastMove("alpha");
                expectedMoves.RemoveAt(expectedMoves.Count - 1);
                expectedMoves.AddRange(PlayBotMoves(live, repo, "alpha", 1));
            }

            // "Restart": open the same file again and rebuild.
            using var reopened = new GameRepository(path);
            var stored = reopened.LoadAll().Should().ContainSingle().Subject;
            stored.Key.Should().Be("alpha");
            stored.CanonicalId.Should().Be("Alpha");
            stored.Moves.Should().Equal(expectedMoves);

            var rebuilt = GameStore.Rebuild(stored.Key, stored.Moves);
            rebuilt.CurrentTurn.Should().Be(live.CurrentTurn);
            rebuilt.State.Should().Be(live.State);
            rebuilt.MainMovePending.Should().Be(live.MainMovePending);
            rebuilt.MoveHistory.Should().Equal(live.MoveHistory);
            rebuilt.Timeline.Count.Should().Be(live.Timeline.Count);
            BoardDiagnostics.Describe(rebuilt.Board).Should().Be(BoardDiagnostics.Describe(live.Board));

            // The undo stack is rebuilt too: exactly as many take-backs as moves.
            for (int i = 0; i < expectedMoves.Count; i++)
                rebuilt.TakeBack().Should().BeTrue($"move {i + 1} of {expectedMoves.Count} should be undoable");
            rebuilt.TakeBack().Should().BeFalse("we are back at the start");
            rebuilt.CurrentTurn.Should().Be(ColorsEnum.Blue);
        }
        finally { DeleteDb(path); }
    }

    [Fact]
    public void DiddilydooSwap_IsReplayedAsPending()
    {
        var path = TempDb();
        try
        {
            var live = new Game();
            var king = live.Board.FindPiece(PiecesEnum.King, ColorsEnum.Blue)!;
            var queen = live.Board.FindPiece(PiecesEnum.Queen, ColorsEnum.Blue)!;
            BoardLocation.IsAdjacent(king.Location, queen.Location).Should().BeTrue("the standard setup allows an opening Diddilydoo");

            using (var repo = new GameRepository(path))
            {
                repo.CreateGame("swap", "swap");
                live.SubmitMove(king.Location.Q, king.Location.R, queen.Location.Q, queen.Location.R);
                live.MainMovePending.Should().BeTrue();
                repo.AppendMove("swap", king.Location.Q, king.Location.R, queen.Location.Q, queen.Location.R);
            }

            using var reopened = new GameRepository(path);
            var stored = reopened.LoadAll().Should().ContainSingle().Subject;
            var rebuilt = GameStore.Rebuild(stored.Key, stored.Moves);
            rebuilt.MainMovePending.Should().BeTrue();
            rebuilt.CurrentTurn.Should().Be(ColorsEnum.Blue);
            BoardDiagnostics.Describe(rebuilt.Board).Should().Be(BoardDiagnostics.Describe(live.Board));
        }
        finally { DeleteDb(path); }
    }

    [Fact]
    public void CreateGame_OnAnExistingKey_StartsOver()
    {
        var path = TempDb();
        try
        {
            using (var repo = new GameRepository(path))
            {
                repo.CreateGame("main", "main");
                PlayBotMoves(new Game(), repo, "main", 3);
                repo.CreateGame("main", "Main");   // the "New game" reset
            }

            using var reopened = new GameRepository(path);
            var stored = reopened.LoadAll().Should().ContainSingle().Subject;
            stored.CanonicalId.Should().Be("Main");
            stored.Moves.Should().BeEmpty();
        }
        finally { DeleteDb(path); }
    }
}
