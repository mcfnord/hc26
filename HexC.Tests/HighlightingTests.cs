using HexC.Engine;
using Xunit;
using System.Linq;

namespace HexC.Tests;

public class HighlightingTests
{
    [Fact]
    public void GetValidMoves_ShouldHighlightDestination_NotReincarnationPortal()
    {
        // Setup: Blue Pawn at (3,-2), Red Pawn at (2,-3).
        // Blue is missing one Pawn (only 2 on board), so one is sidelined.
        // Capturing Red Pawn will trigger reincarnation of the sidelined Blue Pawn at (0,0).
        var board = BoardBuilder.Create()
            .WithPawn(ColorsEnum.Blue, 3, -2)
            .WithPawn(ColorsEnum.Blue, 4, -2) // Second blue pawn
            // Third blue pawn is missing -> sidelined
            .WithPawn(ColorsEnum.Red, 2, -3) // Victim
            .WithKing(ColorsEnum.Blue, 5, -5)
            .WithKing(ColorsEnum.Red, -5, 5)
            .Build();

        var game = new Game();
        game.LoadMatchState(board, ColorsEnum.Blue);

        // Verify Blue Pawn is sidelined
        Assert.True(game.Board.SidelinedPieces.ContainsThePiece(PiecesEnum.Pawn, ColorsEnum.Blue));

        // Get valid moves for Blue Pawn at (3,-2)
        var moves = game.GetValidMoves(3, -2);

        // It should contain (2,-3) - the capture destination
        Assert.Contains(moves, m => m.Q == 2 && m.R == -3);
        
        // It should NOT contain (0,0) - the reincarnation point.
        // If the bug were present, it would see the Add event at (0,0) first and add it.
        Assert.DoesNotContain(moves, m => m.Q == 0 && m.R == 0);
    }
}
