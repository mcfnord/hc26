using HexC.Engine;

namespace HexC.Tests;

/// <summary>
/// Fluent board builder for setting up test scenarios.
/// Usage: var board = BoardBuilder.Create().WithPiece(King, Blue, 0, 1).Build();
/// </summary>
public class BoardBuilder
{
    private readonly List<(PiecesEnum type, ColorsEnum color, int q, int r)> _pieces = new();

    public static BoardBuilder Create() => new();

    public BoardBuilder WithPiece(PiecesEnum type, ColorsEnum color, int q, int r)
    {
        _pieces.Add((type, color, q, r));
        return this;
    }

    public BoardBuilder WithKing(ColorsEnum color, int q, int r) => WithPiece(PiecesEnum.King, color, q, r);
    public BoardBuilder WithQueen(ColorsEnum color, int q, int r) => WithPiece(PiecesEnum.Queen, color, q, r);
    public BoardBuilder WithCastle(ColorsEnum color, int q, int r) => WithPiece(PiecesEnum.Castle, color, q, r);
    public BoardBuilder WithElephant(ColorsEnum color, int q, int r) => WithPiece(PiecesEnum.Elephant, color, q, r);
    public BoardBuilder WithPawn(ColorsEnum color, int q, int r) => WithPiece(PiecesEnum.Pawn, color, q, r);

    /// <summary>
    /// Build a Board with only the specified pieces (no standard setup).
    /// </summary>
    public Board Build()
    {
        var board = new Board();
        foreach (var (type, color, q, r) in _pieces)
            board.Add(new PlacedPiece(type, color, q, r));
        return board;
    }

    /// <summary>
    /// Build a Game loaded with the specified pieces and starting turn.
    /// </summary>
    public Game BuildGame(ColorsEnum startingTurn)
    {
        var game = new Game();
        game.LoadMatchState(Build(), startingTurn);
        return game;
    }
}

/// <summary>
/// Board diagnostic helpers — produces text output that helps an LLM
/// diagnose failures from test runner output alone.
/// </summary>
public static class BoardDiagnostics
{
    /// <summary>
    /// Returns a human-readable summary of all pieces on the board.
    /// Useful in Assert failure messages.
    /// </summary>
    public static string Describe(Board board)
    {
        if (!board.PlacedPieces.Any())
            return "[empty board]";

        var lines = board.PlacedPieces
            .OrderBy(p => p.Color.ToString())
            .ThenBy(p => p.PieceType.ToString())
            .Select(p => $"  {p.Color} {p.PieceType} @ ({p.Location.Q},{p.Location.R})");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Describes what's at a specific location (for assertion messages).
    /// </summary>
    public static string DescribeAt(Board board, int q, int r)
    {
        var piece = board.AnyoneThere(new BoardLocation(q, r));
        return piece == null
            ? $"({q},{r}): empty"
            : $"({q},{r}): {piece.Color} {piece.PieceType}";
    }

    /// <summary>
    /// Lists all sidelined (captured/dead) pieces.
    /// </summary>
    public static string DescribeSidelined(Board board)
    {
        var sidelined = board.SidelinedPieces;
        if (!sidelined.Any())
            return "[no sidelined pieces]";

        var grouped = sidelined
            .GroupBy(p => p.Color)
            .Select(g => $"  {g.Key}: {string.Join(", ", g.Select(p => p.PieceType.ToString()))}");

        return string.Join("\n", grouped);
    }
}
