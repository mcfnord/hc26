using HexC.Engine;
using Xunit;
using Xunit.Abstractions;

namespace HexC.Tests;

/// <summary>
/// Regression tests for checkmate detection.
/// The original bug: GetAttackers used WhatCanICauseWithDoo which filters out
/// king-occupied destinations, so it returned an empty attacker list even when
/// the king was under attack — silently skipping checkmate.
/// </summary>
public class CheckmateDetectionTests
{
    private readonly ITestOutputHelper _output;
    public CheckmateDetectionTests(ITestOutputHelper output) { _output = output; }

    /// <summary>
    /// Reproduces the exact board state from the bug report.
    /// Blue King at (-3,-2) is attacked by White Castle at (-1,-4) along the
    /// -q,+r diagonal AND by Red Queen at (-5,0) along the +q direction.
    /// All escape hexes are blocked or attacked. This is double-check checkmate.
    ///
    /// Setup: place Red Queen one hex away from its final position, make it
    /// Red's turn, then move the Queen into the attacking square. The engine
    /// should detect Blue's checkmate when Blue's turn starts.
    /// </summary>
    [Fact]
    public void DoubleCheck_CastleAndQueen_BlueMated()
    {
        var board = new Board();

        // Blue pieces (victim — King at -3,-2)
        board.Add(new PlacedPiece(PiecesEnum.King,     ColorsEnum.Blue, -3, -2));
        board.Add(new PlacedPiece(PiecesEnum.Queen,    ColorsEnum.Blue, -1,  0));
        board.Add(new PlacedPiece(PiecesEnum.Elephant, ColorsEnum.Blue, -1, -3));
        board.Add(new PlacedPiece(PiecesEnum.Elephant, ColorsEnum.Blue, -2, -2));
        board.Add(new PlacedPiece(PiecesEnum.Elephant, ColorsEnum.Blue, -3, -1));
        board.Add(new PlacedPiece(PiecesEnum.Pawn,     ColorsEnum.Blue, -1, -2));
        board.Add(new PlacedPiece(PiecesEnum.Pawn,     ColorsEnum.Blue, -1, -1));
        board.Add(new PlacedPiece(PiecesEnum.Pawn,     ColorsEnum.Blue, -2, -1));

        // White pieces (one attacker — Castle at -1,-4)
        board.Add(new PlacedPiece(PiecesEnum.King,     ColorsEnum.White,  5, -3));
        board.Add(new PlacedPiece(PiecesEnum.Queen,    ColorsEnum.White,  5, -2));
        board.Add(new PlacedPiece(PiecesEnum.Castle,   ColorsEnum.White, -1, -4));
        board.Add(new PlacedPiece(PiecesEnum.Castle,   ColorsEnum.White,  2,  2));
        board.Add(new PlacedPiece(PiecesEnum.Elephant, ColorsEnum.White,  4, -3));
        board.Add(new PlacedPiece(PiecesEnum.Elephant, ColorsEnum.White,  4, -2));
        board.Add(new PlacedPiece(PiecesEnum.Elephant, ColorsEnum.White,  4, -1));
        board.Add(new PlacedPiece(PiecesEnum.Pawn,     ColorsEnum.White,  3, -2));
        board.Add(new PlacedPiece(PiecesEnum.Pawn,     ColorsEnum.White,  3, -1));
        board.Add(new PlacedPiece(PiecesEnum.Pawn,     ColorsEnum.White,  2, -1));

        // Red pieces — Queen starts at (-5,1) so we can move it to (-5,0)
        board.Add(new PlacedPiece(PiecesEnum.King,     ColorsEnum.Red, -2,  5));
        board.Add(new PlacedPiece(PiecesEnum.Queen,    ColorsEnum.Red, -5,  1));
        board.Add(new PlacedPiece(PiecesEnum.Castle,   ColorsEnum.Red, -1,  5));
        board.Add(new PlacedPiece(PiecesEnum.Castle,   ColorsEnum.Red, -4,  2));
        board.Add(new PlacedPiece(PiecesEnum.Elephant, ColorsEnum.Red, -3,  4));
        board.Add(new PlacedPiece(PiecesEnum.Elephant, ColorsEnum.Red, -2,  4));
        board.Add(new PlacedPiece(PiecesEnum.Elephant, ColorsEnum.Red, -1,  4));
        board.Add(new PlacedPiece(PiecesEnum.Pawn,     ColorsEnum.Red, -2,  3));
        board.Add(new PlacedPiece(PiecesEnum.Pawn,     ColorsEnum.Red, -1,  3));
        board.Add(new PlacedPiece(PiecesEnum.Pawn,     ColorsEnum.Red, -1,  2));

        var game = new Game();
        game.LoadMatchState(board, ColorsEnum.Red);

        // Red Queen moves (-5,1) -> (-5,0), delivering double check with White Castle
        game.SubmitMove(-5, 1, -5, 0);

        _output.WriteLine($"State: {game.State}");
        _output.WriteLine($"Message: {game.StatusMessage}");

        Assert.Equal(GameStateEnum.Finished, game.State);
        Assert.Contains("Checkmate", game.StatusMessage ?? "");
    }

    /// <summary>
    /// Board.GetAttackingColors should correctly identify both attacking colors
    /// in a double-check scenario — this is the core method the fix introduces.
    /// </summary>
    [Fact]
    public void GetAttackingColors_DoubleCheck_ReturnsBothColors()
    {
        var board = BoardBuilder.Create()
            .WithKing(ColorsEnum.Blue, -3, -2)
            .WithCastle(ColorsEnum.White, -1, -4)
            .WithQueen(ColorsEnum.Red, -5, 0)
            .Build();

        var kingLoc = new BoardLocation(-3, -2);
        var attackers = board.GetAttackingColors(kingLoc, ColorsEnum.Blue);

        _output.WriteLine($"Attackers: [{string.Join(", ", attackers)}]");

        Assert.Contains(ColorsEnum.White, attackers);
        Assert.Contains(ColorsEnum.Red, attackers);
        Assert.Equal(2, attackers.Count);
    }

    /// <summary>
    /// GetAttackingColors returns empty when king is not threatened.
    /// </summary>
    [Fact]
    public void GetAttackingColors_NoThreats_ReturnsEmpty()
    {
        var board = BoardBuilder.Create()
            .WithKing(ColorsEnum.Blue, 0, -3)
            .WithCastle(ColorsEnum.White, 5, 0)
            .Build();

        var attackers = board.GetAttackingColors(new BoardLocation(0, -3), ColorsEnum.Blue);
        Assert.Empty(attackers);
    }
}