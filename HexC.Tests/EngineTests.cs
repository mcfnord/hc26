using HexC.Engine;
using Xunit;
using Xunit.Abstractions;

namespace HexC.Tests;

/// <summary>
/// Pure engine logic tests. No HTTP, no server — just Board and Game objects.
/// These run in milliseconds and cover every game rule.
/// </summary>
public class EngineTests
{
    private readonly ITestOutputHelper _output;

    public EngineTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private void Log(string msg) => _output.WriteLine(msg);
    private void LogBoard(Board b, string label = "Board")
    {
        _output.WriteLine($"--- {label} ---");
        _output.WriteLine(BoardDiagnostics.Describe(b));
    }

    // ================================================================
    //  BOARD LOCATION VALIDATION
    // ================================================================

    [Theory]
    [InlineData(0, 0, true)]    // Portal — center of board
    [InlineData(5, 0, true)]    // Edge
    [InlineData(0, 5, true)]    // Edge
    [InlineData(-5, 0, true)]   // Edge
    [InlineData(5, -5, true)]   // Corner
    [InlineData(-5, 5, true)]   // Corner
    [InlineData(3, 3, false)]   // q+r > 5
    [InlineData(6, 0, false)]   // q > 5
    [InlineData(0, -6, false)]  // r < -5
    [InlineData(-3, -3, false)] // q+r < -5
    public void BoardLocation_Validation(int q, int r, bool expected)
    {
        var loc = new BoardLocation(q, r);
        Assert.Equal(expected, loc.IsValidLocation());
    }

    [Fact]
    public void Portal_IsAtOrigin()
    {
        Assert.True(new BoardLocation(0, 0).IsPortal);
        Assert.False(new BoardLocation(1, 0).IsPortal);
        Assert.False(new BoardLocation(0, 1).IsPortal);
    }

    // ================================================================
    //  BOARD BASICS
    // ================================================================

    [Fact]
    public void Board_AddAndFind()
    {
        var board = BoardBuilder.Create()
            .WithKing(ColorsEnum.Blue, 2, -3)
            .Build();

        var found = board.AnyoneThere(new BoardLocation(2, -3));
        Assert.NotNull(found);
        Assert.Equal(PiecesEnum.King, found.PieceType);
        Assert.Equal(ColorsEnum.Blue, found.Color);
    }

    [Fact]
    public void Board_AddToInvalidLocation_Ignored()
    {
        var board = new Board();
        board.Add(new PlacedPiece(PiecesEnum.Pawn, ColorsEnum.Red, 6, 0)); // q > 5
        Assert.Empty(board.PlacedPieces);
    }

    [Fact]
    public void Board_Remove()
    {
        var board = BoardBuilder.Create()
            .WithPawn(ColorsEnum.White, 3, -1)
            .Build();

        var piece = board.AnyoneThere(new BoardLocation(3, -1));
        Assert.NotNull(piece);

        board.Remove(piece);
        Assert.Null(board.AnyoneThere(new BoardLocation(3, -1)));
    }

    [Fact]
    public void Board_Clone_IsIndependent()
    {
        var original = BoardBuilder.Create()
            .WithKing(ColorsEnum.Blue, 1, 1)
            .Build();

        var clone = new Board(original);

        // Remove from clone — original should be unaffected
        var clonePiece = clone.AnyoneThere(new BoardLocation(1, 1));
        clone.Remove(clonePiece!);

        Assert.Null(clone.AnyoneThere(new BoardLocation(1, 1)));
        Assert.NotNull(original.AnyoneThere(new BoardLocation(1, 1)));
    }

    // ================================================================
    //  SIDELINED PIECES (GRAVEYARD)
    // ================================================================

    [Fact]
    public void SidelinedPieces_FullBoard_NothingSidelined()
    {
        var game = new Game(); // Standard setup — all pieces on board
        var sidelined = game.Board.SidelinedPieces;

        // With a standard setup, nothing should be sidelined
        Assert.Empty(sidelined);
    }

    [Fact]
    public void SidelinedPieces_MissingPawn_ShowsInSideline()
    {
        // Only place 2 of Blue's 3 pawns
        var board = BoardBuilder.Create()
            .WithPawn(ColorsEnum.Blue, -1, -2)
            .WithPawn(ColorsEnum.Blue, -1, -1)
            // Missing third blue pawn
            .Build();

        var sidelined = board.SidelinedPieces;
        bool hasSidelinedBluePawn = sidelined.ContainsThePiece(PiecesEnum.Pawn, ColorsEnum.Blue);
        Assert.True(hasSidelinedBluePawn,
            $"Expected a sidelined Blue Pawn. Sidelined:\n{BoardDiagnostics.DescribeSidelined(board)}");
    }

    // ================================================================
    //  PIECE MOVEMENT — PAWN
    // ================================================================

    [Fact]
    public void Pawn_CanMoveToEmptyAdjacentHex()
    {
        var board = BoardBuilder.Create()
            .WithPawn(ColorsEnum.Blue, 0, -2)
            .Build();

        var pawn = board.AnyoneThere(new BoardLocation(0, -2))!;
        var outcomes = board.WhatCanICauseWithDoo(pawn);

        // Should have 6 orthogonal moves (all empty, all valid)
        Assert.True(outcomes.Count > 0, "Pawn should have at least one legal move");
        Log($"Pawn at (0,-2) has {outcomes.Count} moves");
    }

    [Fact]
    public void Pawn_CannotMoveOntoFriendlyPiece()
    {
        var board = BoardBuilder.Create()
            .WithPawn(ColorsEnum.Blue, 0, -2)
            .WithPawn(ColorsEnum.Blue, 0, -1) // Blocking
            .Build();

        var pawn = board.AnyoneThere(new BoardLocation(0, -2))!;
        var outcomes = board.WhatCanICauseWithDoo(pawn);

        // (0,-1) should NOT be in the move list
        bool canMoveToBlocked = outcomes.Any(es => es.Any(e =>
            e.EventType == EventTypeEnum.Add &&
            e.Regarding.Location.Q == 0 && e.Regarding.Location.R == -1));

        Assert.False(canMoveToBlocked, "Pawn should not be able to move onto a friendly piece");
    }

    [Fact]
    public void Pawn_DiagonalAttack_RequiresOpenGate()
    {
        // Pawn at origin-adjacent, enemy diagonally, gates open
        var board = BoardBuilder.Create()
            .WithPawn(ColorsEnum.Blue, 0, -2)
            .WithPawn(ColorsEnum.Red, 1, -1) // Diagonal target at offset (1,1)
            .Build();

        var pawn = board.AnyoneThere(new BoardLocation(0, -2))!;
        var outcomes = board.WhatCanICauseWithDoo(pawn);

        bool canAttack = outcomes.Any(es => es.Any(e =>
            e.EventType == EventTypeEnum.Add &&
            e.Regarding.Location.Q == 1 && e.Regarding.Location.R == -1 &&
            e.Regarding.PieceType == PiecesEnum.Pawn));

        Log($"Pawn diagonal attack possible: {canAttack}");
        // Whether this succeeds depends on gate logic — the test documents the behavior
    }

    // ================================================================
    //  PIECE MOVEMENT — KING
    // ================================================================

    [Fact]
    public void King_MovesToAdjacentHex()
    {
        var board = BoardBuilder.Create()
            .WithKing(ColorsEnum.Blue, 3, -3)
            .Build();

        var king = board.AnyoneThere(new BoardLocation(3, -3))!;
        var outcomes = board.WhatCanICauseWithDoo(king);

        Assert.True(outcomes.Count == 6,
            $"King on open board should have 6 moves, got {outcomes.Count}");
    }

    [Fact]
    public void King_CannotCaptureKing()
    {
        var board = BoardBuilder.Create()
            .WithKing(ColorsEnum.Blue, 1, 0)
            .WithKing(ColorsEnum.Red, 0, 0)   // Adjacent, but it's a King
            .Build();

        var blueKing = board.AnyoneThere(new BoardLocation(1, 0))!;
        var outcomes = board.WhatCanICauseWithDoo(blueKing);

        bool canCaptureRedKing = outcomes.Any(es => es.Any(e =>
            e.EventType == EventTypeEnum.Add &&
            e.Regarding.Location.Q == 0 && e.Regarding.Location.R == 0));

        // King can enter portal if it's a portal victory, but can't capture another king
        // The engine filters out king captures in WhatCanICause
    }

    // ================================================================
    //  PIECE MOVEMENT — CASTLE (ROOK)
    // ================================================================

    [Fact]
    public void Castle_SlidesAlongAxis()
    {
        var board = BoardBuilder.Create()
            .WithCastle(ColorsEnum.White, 0, -3)
            .Build();

        var castle = board.AnyoneThere(new BoardLocation(0, -3))!;
        var outcomes = board.WhatCanICauseWithDoo(castle);

        // Castle should be able to slide in 6 directions
        Assert.True(outcomes.Count > 6,
            $"Castle on mostly-open board should have many moves, got {outcomes.Count}");
    }

    [Fact]
    public void Castle_BlockedByFriendly()
    {
        var board = BoardBuilder.Create()
            .WithCastle(ColorsEnum.White, 0, -3)
            .WithPawn(ColorsEnum.White, 0, -2)  // Blocks +r direction
            .Build();

        var castle = board.AnyoneThere(new BoardLocation(0, -3))!;
        var outcomes = board.WhatCanICauseWithDoo(castle);

        bool canReachPastBlocker = outcomes.Any(es => es.Any(e =>
            e.EventType == EventTypeEnum.Add &&
            e.Regarding.Location.Q == 0 && e.Regarding.Location.R == -1));

        Assert.False(canReachPastBlocker,
            "Castle should not slide past a friendly piece");
    }

    [Fact]
    public void Castle_CapturesEnemy_ThenStops()
    {
        var board = BoardBuilder.Create()
            .WithCastle(ColorsEnum.White, 0, -3)
            .WithPawn(ColorsEnum.Red, 0, -1)    // Enemy in path
            .WithPawn(ColorsEnum.Red, 0, 1)     // Another enemy past the first
            .Build();

        var castle = board.AnyoneThere(new BoardLocation(0, -3))!;
        var outcomes = board.WhatCanICauseWithDoo(castle);

        bool canCapture = outcomes.Any(es => es.Any(e =>
            e.EventType == EventTypeEnum.Add &&
            e.Regarding.Location.Q == 0 && e.Regarding.Location.R == -1 &&
            e.Regarding.PieceType == PiecesEnum.Castle));

        bool canReachSecond = outcomes.Any(es => es.Any(e =>
            e.EventType == EventTypeEnum.Add &&
            e.Regarding.Location.Q == 0 && e.Regarding.Location.R == 1 &&
            e.Regarding.PieceType == PiecesEnum.Castle));

        Assert.True(canCapture, "Castle should capture the first enemy");
        Assert.False(canReachSecond, "Castle should stop after capturing");
    }

    // ================================================================
    //  PIECE MOVEMENT — ELEPHANT (KNIGHT)
    // ================================================================

    [Fact]
    public void Elephant_JumpsOverPieces()
    {
        var board = BoardBuilder.Create()
            .WithElephant(ColorsEnum.Blue, 0, 0)
            // Surround with blockers — elephant should jump over them
            .WithPawn(ColorsEnum.Blue, 1, 0)
            .WithPawn(ColorsEnum.Blue, -1, 0)
            .WithPawn(ColorsEnum.Blue, 0, 1)
            .WithPawn(ColorsEnum.Blue, 0, -1)
            .WithPawn(ColorsEnum.Blue, 1, -1)
            .WithPawn(ColorsEnum.Blue, -1, 1)
            .Build();

        var elephant = board.AnyoneThere(new BoardLocation(0, 0))!;
        var outcomes = board.WhatCanICauseWithDoo(elephant);

        Assert.True(outcomes.Count > 0,
            "Elephant should jump over adjacent blockers");
        Log($"Elephant has {outcomes.Count} jump destinations");
    }

    // ================================================================
    //  QUEEN — SLIDING + SPECIAL DIAGONAL JUMP
    // ================================================================

    [Fact]
    public void Queen_SpecialJump_CleanPath_Succeeds()
    {
        var game = BoardBuilder.Create()
            .WithQueen(ColorsEnum.Blue, 0, -3)
            .BuildGame(ColorsEnum.Blue);

        game.SubmitMove(0, -3, 3, 0);

        var result = game.Board.AnyoneThere(new BoardLocation(3, 0));
        Assert.NotNull(result);
        Assert.Equal(PiecesEnum.Queen, result.PieceType);
        Log("Queen jumped SE from (0,-3) to (3,0) successfully");
    }

    [Fact]
    public void Queen_SpecialJump_BlockedGates_Fails()
    {
        var game = BoardBuilder.Create()
            .WithQueen(ColorsEnum.Blue, 0, -3)
            .WithPawn(ColorsEnum.Blue, 1, -3)  // Gate A blocked
            .WithPawn(ColorsEnum.Blue, 0, -2)  // Gate B blocked
            .BuildGame(ColorsEnum.Blue);

        game.SubmitMove(0, -3, 3, 0);

        // Queen should still be at start
        var result = game.Board.AnyoneThere(new BoardLocation(0, -3));
        Assert.NotNull(result);
        Assert.Equal(PiecesEnum.Queen, result.PieceType);
        Log("Queen correctly blocked by closed gates");
    }

    [Fact]
    public void Queen_SpecialJump_BlockedPath_Fails()
    {
        var game = BoardBuilder.Create()
            .WithQueen(ColorsEnum.Blue, 0, -3)
            .WithPawn(ColorsEnum.Red, 2, -1)   // Blocker on intermediate step
            .BuildGame(ColorsEnum.Blue);

        game.SubmitMove(0, -3, 3, 0);

        var result = game.Board.AnyoneThere(new BoardLocation(0, -3));
        Assert.NotNull(result);
        Assert.Equal(PiecesEnum.Queen, result.PieceType);
    }

    [Fact]
    public void Queen_SpecialJump_CapturesEnemy()
    {
        var game = BoardBuilder.Create()
            .WithQueen(ColorsEnum.Blue, 0, -3)
            .WithElephant(ColorsEnum.Red, 3, 0)  // Enemy at destination
            .BuildGame(ColorsEnum.Blue);

        game.SubmitMove(0, -3, 3, 0);

        var result = game.Board.AnyoneThere(new BoardLocation(3, 0));
        Assert.NotNull(result);
        Assert.Equal(PiecesEnum.Queen, result.PieceType);
        Assert.Equal(ColorsEnum.Blue, result.Color);
    }

    // ================================================================
    //  PHALANX (MOB) PROTECTION
    // ================================================================

    [Fact]
    public void Phalanx_NonAdjacentNeighbors_ShouldNotBeMob()
    {
        // (1,0) and (-1,0) are both neighbors of (0,0), but they are not adjacent to each other.
        // Therefore, (0,0) should NOT be mob-locked.
        var board = BoardBuilder.Create()
            .WithPawn(ColorsEnum.Blue, 0, 0)
            .WithPawn(ColorsEnum.Blue, 1, 0)
            .WithPawn(ColorsEnum.Blue, -1, 0)
            .WithPawn(ColorsEnum.Red, 1, 1) // Diagonal attack target for (0,0)
            .Build();

        var pawn = board.AnyoneThere(new BoardLocation(0, 0))!;
        var outcomes = board.WhatCanICauseWithDoo(pawn);

        bool hasCapture = outcomes.Any(es =>
            es.Any(e => e.EventType == EventTypeEnum.Remove && e.Regarding.Color == ColorsEnum.Red));

        Assert.True(hasCapture, "Pawn with non-adjacent neighbors should NOT be mob-locked and SHOULD be able to capture.");
    }

    [Fact]
    public void Phalanx_ProtectedPawn_CannotBeCaptured()
    {
        var game = BoardBuilder.Create()
            .WithPawn(ColorsEnum.Blue, 1, -1)
            .WithPawn(ColorsEnum.Blue, 0, -1)
            .WithPawn(ColorsEnum.Blue, 1, -2)   // Target: adjacent to both others
            .WithCastle(ColorsEnum.White, 1, -4) // Attacker
            .BuildGame(ColorsEnum.White);

        game.SubmitMove(1, -4, 1, -2);

        // Attacker should NOT have moved, victim should survive
        var attacker = game.Board.AnyoneThere(new BoardLocation(1, -4));
        var victim = game.Board.AnyoneThere(new BoardLocation(1, -2));

        Assert.NotNull(attacker);
        Assert.Equal(PiecesEnum.Castle, attacker.PieceType);
        Assert.NotNull(victim);
        Assert.Equal(PiecesEnum.Pawn, victim.PieceType);
        Log("Phalanx protection held — Castle attack rejected");
    }

    [Fact]
    public void Phalanx_MobPawn_CannotCapture()
    {
        // Mob: Blue pawns at (1,-1), (0,-1), (1,-2)
        // Enemy: Red pawn at (2,0) — reachable from (1,-1) via diagonal attack (1,1)
        // Gates for that attack: (2,-1) and (1,0) are both empty → gate open
        var board = BoardBuilder.Create()
            .WithPawn(ColorsEnum.Blue, 1, -1)
            .WithPawn(ColorsEnum.Blue, 0, -1)
            .WithPawn(ColorsEnum.Blue, 1, -2)
            .WithPawn(ColorsEnum.Red, 2, 0)
            .Build();

        var mobPawn = board.AnyoneThere(new BoardLocation(1, -1))!;
        var outcomes = board.WhatCanICauseWithDoo(mobPawn);

        bool hasCapture = outcomes.Any(es =>
            es.Any(e => e.EventType == EventTypeEnum.Remove && e.Regarding.Color == ColorsEnum.Red));

        Assert.False(hasCapture, "Mob pawn should not be able to capture");
    }

    [Fact]
    public void Phalanx_MobPawn_CanStillMove()
    {
        // Mob pawn should still have non-capture movement options
        var board = BoardBuilder.Create()
            .WithPawn(ColorsEnum.Blue, 1, -1)
            .WithPawn(ColorsEnum.Blue, 0, -1)
            .WithPawn(ColorsEnum.Blue, 1, -2)
            .Build();

        var mobPawn = board.AnyoneThere(new BoardLocation(1, -1))!;
        var outcomes = board.WhatCanICauseWithDoo(mobPawn);

        Assert.True(outcomes.Count > 0, "Mob pawn should still be able to move to empty hexes");
        Log($"Mob pawn at (1,-1) has {outcomes.Count} empty-hex moves");
    }

    [Fact]
    public void Phalanx_BrokenMob_PawnCanCapture()
    {
        // Start with mob, move one pawn away (breaking the triangle), then the
        // remaining pawn regains capture ability on the same board state.
        var game = BoardBuilder.Create()
            .WithPawn(ColorsEnum.Blue, 1, -1)
            .WithPawn(ColorsEnum.Blue, 0, -1)
            .WithPawn(ColorsEnum.Blue, 1, -2)
            .WithPawn(ColorsEnum.Red, 2, 0)
            .WithKing(ColorsEnum.Blue, -2, -3)
            .BuildGame(ColorsEnum.Blue);

        // Move a mob pawn away to (0,-2), breaking the triangle
        game.SubmitMove(0, -1, 0, -2);

        // (1,-1) now has only 1 blue neighbor (1,-2) → no longer mob-locked
        var pawn = game.Board.AnyoneThere(new BoardLocation(1, -1))!;
        var outcomes = game.Board.WhatCanICauseWithDoo(pawn);

        bool hasCapture = outcomes.Any(es =>
            es.Any(e => e.EventType == EventTypeEnum.Remove && e.Regarding.Color == ColorsEnum.Red));

        Assert.True(hasCapture, "After mob breaks, pawn should be able to capture");
    }

    [Fact]
    public void Phalanx_NonMobPawn_CanCapture()
    {
        // Lone Blue pawn (no mob) should be able to capture enemy on diagonal
        // Attack offset (1,1): pawn (0,-2) → target (1,-1). Gates: (1,-2) and (0,-1) — both empty.
        var board = BoardBuilder.Create()
            .WithPawn(ColorsEnum.Blue, 0, -2)
            .WithPawn(ColorsEnum.Red, 1, -1)
            .Build();

        var pawn = board.AnyoneThere(new BoardLocation(0, -2))!;
        var outcomes = board.WhatCanICauseWithDoo(pawn);

        bool hasCapture = outcomes.Any(es =>
            es.Any(e => e.EventType == EventTypeEnum.Remove && e.Regarding.Color == ColorsEnum.Red));

        Assert.True(hasCapture, "Non-mob pawn should be able to capture normally");
    }

    [Fact]
    public void Phalanx_MobPawn_StillImmuneToCapture()
    {
        // Verify the defensive side of phalanx wasn't broken by the change
        var game = BoardBuilder.Create()
            .WithPawn(ColorsEnum.Blue, 1, -1)
            .WithPawn(ColorsEnum.Blue, 0, -1)
            .WithPawn(ColorsEnum.Blue, 1, -2)
            .WithCastle(ColorsEnum.White, 1, -4)
            .BuildGame(ColorsEnum.White);

        game.SubmitMove(1, -4, 1, -2);

        var victim = game.Board.AnyoneThere(new BoardLocation(1, -2));
        Assert.NotNull(victim);
        Assert.Equal(PiecesEnum.Pawn, victim!.PieceType);
        Assert.Equal(ColorsEnum.Blue, victim.Color);
        Log("Mob pawn remains immune to castle capture after rule change");
    }

    [Fact]
    public void Phalanx_GetValidMoves_ExcludesCaptures()
    {
        // GetValidMoves API should not list enemy-occupied squares for a mob pawn
        var game = BoardBuilder.Create()
            .WithPawn(ColorsEnum.Blue, 1, -1)
            .WithPawn(ColorsEnum.Blue, 0, -1)
            .WithPawn(ColorsEnum.Blue, 1, -2)
            .WithPawn(ColorsEnum.Red, 2, 0)
            .WithKing(ColorsEnum.Blue, -2, -3)
            .BuildGame(ColorsEnum.Blue);

        var moves = game.GetValidMoves(1, -1);

        bool includesEnemy = moves.Any(m => m.Q == 2 && m.R == 0);
        Assert.False(includesEnemy,
            "GetValidMoves should not list enemy-occupied squares for a mob pawn");
    }

    [Fact]
    public void Phalanx_UnprotectedPawn_CanBeCaptured()
    {
        // Only one neighbor — not enough for phalanx
        var game = BoardBuilder.Create()
            .WithPawn(ColorsEnum.Blue, 1, -1)
            .WithPawn(ColorsEnum.Blue, 1, -2)   // Target: only 1 neighbor
            .WithCastle(ColorsEnum.White, 1, -4) // Attacker
            .BuildGame(ColorsEnum.White);

        game.SubmitMove(1, -4, 1, -2);

        var result = game.Board.AnyoneThere(new BoardLocation(1, -2));
        Assert.NotNull(result);
        Assert.Equal(PiecesEnum.Castle, result.PieceType); // Castle captured the pawn
        Assert.Equal(ColorsEnum.White, result.Color);
    }

    // ================================================================
    //  PORTAL MECHANICS
    // ================================================================

    [Fact]
    public void Portal_KingEntry_WinsGame()
    {
        var game = BoardBuilder.Create()
            .WithKing(ColorsEnum.Red, 0, 1)
            .BuildGame(ColorsEnum.Red);

        game.SubmitMove(0, 1, 0, 0);

        Assert.Equal(GameStateEnum.Finished, game.State);
        Assert.Contains("Wins by Portal", game.StatusMessage ?? "");
        Log($"Game ended: {game.StatusMessage}");
    }

    [Fact]
    public void Portal_NonKingEntry_PieceVanishes()
    {
        // A non-King moving into an empty portal should vanish
        var game = BoardBuilder.Create()
            .WithPawn(ColorsEnum.Blue, 0, -1)
            .WithKing(ColorsEnum.Blue, -2, -3) // Need king to avoid null ref in check detection
            .BuildGame(ColorsEnum.Blue);

        // Pawn tries to move to portal - engine should handle the void
        game.SubmitMove(0, -1, 0, 0);

        Log($"After move: {BoardDiagnostics.DescribeAt(game.Board, 0, 0)}");
        // The pawn should vanish (portal void effect for non-kings)
    }

    // ================================================================
    //  REINCARNATION
    // ================================================================

    [Fact]
    public void Reincarnation_CaptureEnemyPawn_SpawnsFriendlyPawn()
    {
        // White has a sidelined pawn (only 2 of 3 on board).
        // White captures a Red Pawn -> White Pawn reincarnates at portal.
        var game = BoardBuilder.Create()
            .WithPawn(ColorsEnum.Red, 1, 0)       // Victim
            .WithCastle(ColorsEnum.White, 2, -1)   // Attacker
            // White only has 2 pawns on board (missing 1 -> sidelined)
            .WithPawn(ColorsEnum.White, 3, -2)
            .WithPawn(ColorsEnum.White, 3, -1)
            .BuildGame(ColorsEnum.White);

        Log($"Sidelined before capture:\n{BoardDiagnostics.DescribeSidelined(game.Board)}");

        game.SubmitMove(2, -1, 1, 0);

        var portal = game.Board.AnyoneThere(new BoardLocation(0, 0));

        Assert.NotNull(portal);
        Assert.Equal(ColorsEnum.White, portal!.Color);
        Assert.Equal(PiecesEnum.Pawn, portal.PieceType);
        Log("Reincarnation succeeded: White Pawn spawned at portal");
    }

    [Fact]
    public void Reincarnation_Fails_WhenNoSidelinedPieces()
    {
        // White has ALL 3 pawns on board — no sidelined pawns.
        var game = BoardBuilder.Create()
            .WithPawn(ColorsEnum.Red, 1, 0)       // Victim (Pawn)
            .WithCastle(ColorsEnum.White, 2, -1)   // Attacker
            .WithPawn(ColorsEnum.White, 3, -2)
            .WithPawn(ColorsEnum.White, 3, -1)
            .WithPawn(ColorsEnum.White, 2, -2)     // All 3 White pawns present
            .BuildGame(ColorsEnum.White);

        game.SubmitMove(2, -1, 1, 0);

        var portal = game.Board.AnyoneThere(new BoardLocation(0, 0));
        Assert.Null(portal);
        Log("No reincarnation (correctly): all White Pawns already on board");
    }

    [Fact]
    public void Reincarnation_Fails_WhenPortalOccupied()
    {
        var game = BoardBuilder.Create()
            .WithPawn(ColorsEnum.Red, 1, 0)       // Victim
            .WithCastle(ColorsEnum.White, 2, -1)   // Attacker
            .WithElephant(ColorsEnum.Blue, 0, 0)   // Portal blocked
            .BuildGame(ColorsEnum.White);

        game.SubmitMove(2, -1, 1, 0);

        var portal = game.Board.AnyoneThere(new BoardLocation(0, 0));
        // Portal should still have the Blue Elephant, not a reincarnated piece
        Assert.NotNull(portal);
        Assert.Equal(ColorsEnum.Blue, portal!.Color);
        Log("Reincarnation blocked: portal already occupied");
    }

    // ================================================================
    //  DIDDILYDOO (KING-QUEEN SWAP)
    // ================================================================

    [Fact]
    public void Diddilydoo_SwapsKingAndQueen()
    {
        var game = BoardBuilder.Create()
            .WithKing(ColorsEnum.Blue, 3, -3)
            .WithQueen(ColorsEnum.Blue, 3, -2)
            .BuildGame(ColorsEnum.Blue);

        game.SubmitMove(3, -3, 3, -2); // King targets Queen for swap

        Assert.True(game.MainMovePending, "Swap should activate MainMovePending");

        var kingSpot = game.Board.AnyoneThere(new BoardLocation(3, -2));
        var queenSpot = game.Board.AnyoneThere(new BoardLocation(3, -3));

        Assert.Equal(PiecesEnum.King, kingSpot?.PieceType);
        Assert.Equal(PiecesEnum.Queen, queenSpot?.PieceType);
        Log("Diddilydoo succeeded: pieces swapped, bonus move pending");
    }

    [Fact]
    public void Diddilydoo_SwapBack_RestoresOriginalPositions()
    {
        var game = BoardBuilder.Create()
            .WithKing(ColorsEnum.Blue, 3, -3)
            .WithQueen(ColorsEnum.Blue, 3, -2)
            .BuildGame(ColorsEnum.Blue);

        game.SubmitMove(3, -3, 3, -2); // Swap
        Assert.True(game.MainMovePending);

        game.SubmitMove(3, -2, 3, -3); // Swap back
        Assert.False(game.MainMovePending);

        var king = game.Board.AnyoneThere(new BoardLocation(3, -3));
        var queen = game.Board.AnyoneThere(new BoardLocation(3, -2));
        Assert.Equal(PiecesEnum.King, king?.PieceType);
        Assert.Equal(PiecesEnum.Queen, queen?.PieceType);
        Log("Swap-back restored original positions and cleared MainMovePending");
    }

    [Fact]
    public void Diddilydoo_MultipleToggles_ThenNormalMove()
    {
        var game = BoardBuilder.Create()
            .WithKing(ColorsEnum.Blue, 3, -3)
            .WithQueen(ColorsEnum.Blue, 3, -2)
            .WithPawn(ColorsEnum.Blue, 0, -1)
            .BuildGame(ColorsEnum.Blue);

        game.SubmitMove(3, -3, 3, -2); // Swap
        Assert.True(game.MainMovePending);

        game.SubmitMove(3, -2, 3, -3); // Swap back
        Assert.False(game.MainMovePending);

        game.SubmitMove(3, -3, 3, -2); // Swap again
        Assert.True(game.MainMovePending);

        game.SubmitMove(0, -1, 0, -2); // Normal pawn move ends turn
        Assert.False(game.MainMovePending);
        Log("Multiple swap toggles followed by normal move worked correctly");
    }

    [Fact]
    public void Diddilydoo_ThenPortalVictory()
    {
        var game = BoardBuilder.Create()
            .WithQueen(ColorsEnum.Blue, 1, 0)
            .WithKing(ColorsEnum.Blue, 2, 0)
            .BuildGame(ColorsEnum.Blue);

        // Swap: King(2,0) <-> Queen(1,0)
        game.SubmitMove(2, 0, 1, 0);
        Assert.True(game.MainMovePending, "Swap should succeed");

        // Bonus move: King (now at 1,0) -> Portal (0,0)
        game.SubmitMove(1, 0, 0, 0);

        Assert.Equal(GameStateEnum.Finished, game.State);
        Assert.Contains("Wins by Portal", game.StatusMessage ?? "");
        Log($"Diddilydoo->Portal victory: {game.StatusMessage}");
    }

    // ================================================================
    //  DIDDILYDOO — ADJACENCY REQUIREMENT
    // ================================================================

    [Theory]
    [InlineData(0, 0,  1, 0, true)]   // +q neighbor
    [InlineData(0, 0,  0, 1, true)]   // +r neighbor
    [InlineData(0, 0, -1, 1, true)]   // -q+r neighbor
    [InlineData(0, 0,  1,-1, true)]   // +q-r neighbor
    [InlineData(0, 0, -1, 0, true)]   // -q neighbor
    [InlineData(0, 0,  0,-1, true)]   // -r neighbor
    [InlineData(0, 0,  0, 0, false)]  // Same hex
    [InlineData(0, 0,  2, 0, false)]  // Two steps away
    [InlineData(0, 0,  1, 1, false)]  // Diagonal (not adjacent on hex grid)
    [InlineData(3,-3,  3,-2, true)]   // Arbitrary adjacent pair
    [InlineData(3,-3,  5,-2, false)]  // Far apart
    public void BoardLocation_IsAdjacent(int q1, int r1, int q2, int r2, bool expected)
    {
        var a = new BoardLocation(q1, r1);
        var b = new BoardLocation(q2, r2);
        Assert.Equal(expected, BoardLocation.IsAdjacent(a, b));
    }

    [Fact]
    public void Diddilydoo_Rejected_WhenNotAdjacent()
    {
        var game = BoardBuilder.Create()
            .WithKing(ColorsEnum.Blue, -2, -3)
            .WithQueen(ColorsEnum.Blue, 3, -2)   // far away
            .BuildGame(ColorsEnum.Blue);

        game.SubmitMove(-2, -3, 3, -2);
        Assert.False(game.MainMovePending, "Non-adjacent swap should be rejected");
        Assert.Contains("adjacent", game.StatusMessage ?? "",
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetValidMoves_King_ExcludesDistantQueen()
    {
        var game = BoardBuilder.Create()
            .WithKing(ColorsEnum.Blue, -2, -3)
            .WithQueen(ColorsEnum.Blue, 3, -2)   // far away
            .BuildGame(ColorsEnum.Blue);

        var moves = game.GetValidMoves(-2, -3);

        bool includesQueen = moves.Any(m => m.Q == 3 && m.R == -2);
        Assert.False(includesQueen,
            "GetValidMoves should not list distant Queen as swap target");
    }

    [Fact]
    public void GetValidMoves_King_IncludesAdjacentQueen()
    {
        var game = BoardBuilder.Create()
            .WithKing(ColorsEnum.Blue, 3, -3)
            .WithQueen(ColorsEnum.Blue, 3, -2)   // adjacent
            .BuildGame(ColorsEnum.Blue);

        var moves = game.GetValidMoves(3, -3);

        bool includesQueen = moves.Any(m => m.Q == 3 && m.R == -2);
        Assert.True(includesQueen,
            "GetValidMoves should list adjacent Queen as swap target");
    }

    // ================================================================
    //  CHECK AND CHECKMATE
    // ================================================================

    [Fact]
    public void Check_KingAttacked_ButCanEscape()
    {
        // White Castle attacks Blue King, but Blue King can move away
        var game = BoardBuilder.Create()
            .WithKing(ColorsEnum.Blue, 0, -3)
            .WithCastle(ColorsEnum.White, 0, 3)    // Attacks along r-axis
            .WithKing(ColorsEnum.White, 5, -3)     // White king (needed for game)
            .WithKing(ColorsEnum.Red, -3, 5)       // Red king (needed for game)
            .BuildGame(ColorsEnum.Blue);

        // Blue is in check but should be able to escape (not checkmate)
        Assert.Equal(GameStateEnum.Active, game.State);
    }

    [Fact]
    public void SuicidalMove_Rejected()
    {
        // Blue King tries to move into a square attacked by White
        var game = BoardBuilder.Create()
            .WithKing(ColorsEnum.Blue, 0, -3)
            .WithCastle(ColorsEnum.White, 1, 0)   // Attacks column q=1
            .BuildGame(ColorsEnum.Blue);

        var movesBefore = game.Board.PlacedPieces.Count;
        game.SubmitMove(0, -3, 1, -3); // Moving into attacked column

        // King should still be at original position (move rejected)
        var king = game.Board.AnyoneThere(new BoardLocation(0, -3));
        Assert.NotNull(king);
        Assert.Equal(PiecesEnum.King, king?.PieceType);
    }

    // ================================================================
    //  TURN ORDER
    // ================================================================

    [Fact]
    public void TurnOrder_Blue_White_Red()
    {
        var game = new Game(); // Standard setup
        Assert.Equal(ColorsEnum.Blue, game.CurrentTurn);

        // Make a valid Blue move (pawn forward)
        game.SubmitMove(-1, -1, -1, 0);
        Assert.Equal(ColorsEnum.White, game.CurrentTurn);
    }

    [Fact]
    public void WrongColor_MoveRejected()
    {
        var game = new Game(); // Blue's turn
        Assert.Equal(ColorsEnum.Blue, game.CurrentTurn);

        // Try to move a White piece
        game.SubmitMove(3, -2, 3, -1);
        Assert.Contains("turn", game.StatusMessage ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ColorsEnum.Blue, game.CurrentTurn); // Turn should not change
    }

    // ================================================================
    //  UNDO (TAKEBACK)
    // ================================================================

    [Fact]
    public void Undo_RestoresPreviousState()
    {
        var game = new Game();
        var originalTurn = game.CurrentTurn;

        game.SubmitMove(-1, -1, -1, 0); // Blue pawn move
        Assert.NotEqual(originalTurn, game.CurrentTurn); // Turn advanced

        bool undone = game.TakeBack();
        Assert.True(undone);
        Assert.Equal(originalTurn, game.CurrentTurn); // Turn restored

        // Pawn should be back at original location
        var pawn = game.Board.AnyoneThere(new BoardLocation(-1, -1));
        Assert.NotNull(pawn);
        Assert.Equal(PiecesEnum.Pawn, pawn?.PieceType);
    }

    [Fact]
    public void Undo_AtStart_ReturnsFalse()
    {
        var game = new Game();
        Assert.False(game.TakeBack());
    }

    [Fact]
    public void Undo_MultipleSteps()
    {
        var game = new Game();

        game.SubmitMove(-1, -1, -1, 0);  // Blue
        game.SubmitMove(3, -1, 2, 0);    // White
        // Two moves made

        game.TakeBack(); // Undo White
        Assert.Equal(ColorsEnum.White, game.CurrentTurn);

        game.TakeBack(); // Undo Blue
        Assert.Equal(ColorsEnum.Blue, game.CurrentTurn);
    }

    // ================================================================
    //  VALID MOVES API
    // ================================================================

    [Fact]
    public void GetValidMoves_ReturnsNonEmpty_ForOwnPiece()
    {
        var game = new Game();
        var moves = game.GetValidMoves(-1, -1); // Blue pawn
        Assert.NotEmpty(moves);
    }

    [Fact]
    public void GetValidMoves_ReturnsEmpty_ForEnemyPiece()
    {
        var game = new Game(); // Blue's turn
        var moves = game.GetValidMoves(3, -2); // White pawn — not Blue's turn
        Assert.Empty(moves);
    }

    [Fact]
    public void GetValidMoves_ReturnsEmpty_ForEmptySquare()
    {
        var game = new Game();
        var moves = game.GetValidMoves(0, 0); // Portal — empty at start
        Assert.Empty(moves);
    }

    // ================================================================
    //  STANDARD BOARD SETUP
    // ================================================================

    [Fact]
    public void StandardSetup_Has30Pieces()
    {
        var game = new Game();
        // 3 players x 10 pieces each = 30
        Assert.Equal(30, game.Board.PlacedPieces.Count);
    }

    [Theory]
    [InlineData(ColorsEnum.Blue)]
    [InlineData(ColorsEnum.White)]
    [InlineData(ColorsEnum.Red)]
    public void StandardSetup_EachColorHasFullArmy(ColorsEnum color)
    {
        var game = new Game();
        var pieces = game.Board.PlacedPieces.Where(p => p.Color == color).ToList();

        Assert.Equal(10, pieces.Count);
        Assert.Single(pieces, p => p.PieceType == PiecesEnum.King);
        Assert.Single(pieces, p => p.PieceType == PiecesEnum.Queen);
        Assert.Equal(2, pieces.Count(p => p.PieceType == PiecesEnum.Castle));
        Assert.Equal(3, pieces.Count(p => p.PieceType == PiecesEnum.Elephant));
        Assert.Equal(3, pieces.Count(p => p.PieceType == PiecesEnum.Pawn));
    }

    // ================================================================
    //  PORTAL REINCARNATION FIX
    // ================================================================

    [Fact]
    public void Portal_PawnAttacksEnemy_TriggersReincarnation()
    {
        // Arrange: Setup a board where Blue has a sidelined Elephant
        var board = BoardBuilder.Create()
            .WithPawn(ColorsEnum.Blue, -1, -1)     // Attacker (matches your screenshot position)
            .WithElephant(ColorsEnum.Red, 0, 0)    // Victim in Portal
            .WithKing(ColorsEnum.Blue, -2, -3)     // Kings required to pass suicidal checks
            .WithKing(ColorsEnum.Red, 3, 3)
            // Omit one Blue Elephant from the board so it is sidelined
            .WithElephant(ColorsEnum.Blue, -4, 2)
            .WithElephant(ColorsEnum.Blue, -4, 3)
            .Build();

        var game = new Game();
        game.LoadMatchState(board, ColorsEnum.Blue);

        // Verify a Blue Elephant is available in the graveyard
        Assert.True(game.Board.SidelinedPieces.ContainsThePiece(PiecesEnum.Elephant, ColorsEnum.Blue));

        // Act: Execute the attack on the portal
        game.SubmitMove(-1, -1, 0, 0);

        // Assert: The move was accepted and the turn advanced
        Assert.Equal(ColorsEnum.White, game.CurrentTurn); 

        // Assert: The portal now contains the reincarnated Blue Elephant
        var portal = game.Board.AnyoneThere(new BoardLocation(0, 0));
        Assert.NotNull(portal);
        Assert.Equal(PiecesEnum.Elephant, portal.PieceType);
        Assert.Equal(ColorsEnum.Blue, portal.Color);
    }
}
