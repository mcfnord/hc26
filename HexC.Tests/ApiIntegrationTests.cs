using System.Net;
using System.Net.Http.Json;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace HexC.Tests;

/// <summary>
/// Integration tests that exercise the real ASP.NET Core pipeline in-process.
/// Each test gets its own game ID to avoid cross-contamination.
/// </summary>
public class ApiIntegrationTests : IClassFixture<HexChessWebFactory>
{
    private readonly HttpClient _client;
    private readonly ITestOutputHelper _output;
    private static int _gameCounter = 0;

    public ApiIntegrationTests(HexChessWebFactory factory, ITestOutputHelper output)
    {
        _client = factory.CreateClient();
        _output = output;
    }

    private string NewGameId() => $"test_{Interlocked.Increment(ref _gameCounter)}_{Guid.NewGuid():N}";

    private void Log(string msg) => _output.WriteLine(msg);

    // ================================================================
    //  GAME CREATION
    // ================================================================

    [Fact]
    public async Task CreateGame_ReturnsOk()
    {
        var gameId = NewGameId();
        var response = await _client.PostAsync($"/Game/create?gameId={gameId}", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Log($"Create response: {body}");
    }

    [Fact]
    public async Task CreateGame_DuplicateId_ReturnsConflict()
    {
        var gameId = NewGameId();
        await _client.PostAsync($"/Game/create?gameId={gameId}", null);

        var response = await _client.PostAsync($"/Game/create?gameId={gameId}", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateGame_EmptyId_ReturnsBadRequest()
    {
        var response = await _client.PostAsync("/Game/create?gameId=", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ================================================================
    //  GAME STATUS
    // ================================================================

    [Fact]
    public async Task GetStatus_NewGame_ShowsBlueTurn()
    {
        var gameId = NewGameId();
        await _client.PostAsync($"/Game/create?gameId={gameId}", null);

        var response = await _client.GetAsync($"/Game/status?gameId={gameId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var status = await ParseJson<StatusResponse>(response);
        Assert.Equal("Blue", status.Turn);
        Assert.Equal("Active", status.State);
        Log($"Status: {status.Turn} / {status.State} / {status.Message}");
    }

    [Fact]
    public async Task GetStatus_NonexistentGame_Returns404()
    {
        var response = await _client.GetAsync("/Game/status?gameId=does_not_exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ================================================================
    //  BOARD STATE
    // ================================================================

    [Fact]
    public async Task GetBoard_NewGame_Returns30Pieces()
    {
        var gameId = NewGameId();
        await _client.PostAsync($"/Game/create?gameId={gameId}", null);

        var response = await _client.GetAsync($"/Game/board?gameId={gameId}");
        var pieces = await ParseJson<List<PieceResponse>>(response);

        Assert.Equal(30, pieces.Count);
        Log($"Board has {pieces.Count} pieces");
    }

    [Fact]
    public async Task GetBoard_NewGame_HasAllFactions()
    {
        var gameId = NewGameId();
        await _client.PostAsync($"/Game/create?gameId={gameId}", null);

        var pieces = await GetBoard(gameId);

        var blue = pieces.Count(p => p.Color == "Blue");
        var white = pieces.Count(p => p.Color == "White");
        var red = pieces.Count(p => p.Color == "Red");

        Assert.Equal(10, blue);
        Assert.Equal(10, white);
        Assert.Equal(10, red);
    }

    // ================================================================
    //  VALID MOVES
    // ================================================================

    [Fact]
    public async Task GetValidMoves_BluePawn_ReturnsOptions()
    {
        var gameId = NewGameId();
        await _client.PostAsync($"/Game/create?gameId={gameId}", null);

        var response = await _client.GetAsync($"/Game/validMoves?gameId={gameId}&q=-1&r=-1");
        var moves = await ParseJson<List<MoveResponse>>(response);

        Assert.NotEmpty(moves);
        Log($"Blue Pawn at (-1,-1) has {moves.Count} valid moves: {string.Join(", ", moves.Select(m => $"({m.Q},{m.R})"))}");
    }

    [Fact]
    public async Task GetValidMoves_EnemyPiece_ReturnsEmpty()
    {
        var gameId = NewGameId();
        await _client.PostAsync($"/Game/create?gameId={gameId}", null);

        // It's Blue's turn — asking for White piece moves should return empty
        var response = await _client.GetAsync($"/Game/validMoves?gameId={gameId}&q=3&r=-2");
        var moves = await ParseJson<List<MoveResponse>>(response);

        Assert.Empty(moves);
    }

    [Fact]
    public async Task GetValidMoves_EmptySquare_ReturnsEmpty()
    {
        var gameId = NewGameId();
        await _client.PostAsync($"/Game/create?gameId={gameId}", null);

        var response = await _client.GetAsync($"/Game/validMoves?gameId={gameId}&q=0&r=0");
        var moves = await ParseJson<List<MoveResponse>>(response);

        Assert.Empty(moves);
    }

    // ================================================================
    //  MOVE SUBMISSION
    // ================================================================

    [Fact]
    public async Task Move_ValidBluePawn_AdvancesToWhiteTurn()
    {
        var gameId = NewGameId();
        await _client.PostAsync($"/Game/create?gameId={gameId}", null);

        // Move Blue Pawn from (-1,-1) to (-1,0)
        var moveRes = await SubmitMove(gameId, -1, -1, -1, 0);
        Assert.True(moveRes.Success, $"Move failed: {moveRes.Message}");

        var status = await GetStatus(gameId);
        Assert.Equal("White", status.Turn);
        Log($"After Blue move: turn={status.Turn}, msg={status.Message}");
    }

    [Fact]
    public async Task Move_InvalidDestination_ReturnsBadRequest()
    {
        var gameId = NewGameId();
        await _client.PostAsync($"/Game/create?gameId={gameId}", null);

        // Try to move Blue pawn to an invalid location
        var response = await _client.PostAsync(
            $"/Game/move?gameId={gameId}&q1=-1&r1=-1&q2=5&r2=5", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Move_WrongTurn_ReturnsBadRequest()
    {
        var gameId = NewGameId();
        await _client.PostAsync($"/Game/create?gameId={gameId}", null);

        // Try moving White piece on Blue's turn
        var response = await _client.PostAsync(
            $"/Game/move?gameId={gameId}&q1=3&r1=-2&q2=3&r2=-1", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Move_EmptySquare_ReturnsBadRequest()
    {
        var gameId = NewGameId();
        await _client.PostAsync($"/Game/create?gameId={gameId}", null);

        var response = await _client.PostAsync(
            $"/Game/move?gameId={gameId}&q1=0&r1=0&q2=0&r2=1", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ================================================================
    //  MULTI-TURN GAME FLOW
    // ================================================================

    [Fact]
    public async Task FullTurnCycle_Blue_White_Red_Blue()
    {
        var gameId = NewGameId();
        await _client.PostAsync($"/Game/create?gameId={gameId}", null);

        // Blue: Pawn (-1,-1) -> (-1,0)
        var r1 = await SubmitMove(gameId, -1, -1, -1, 0);
        Assert.True(r1.Success, $"Blue move failed: {r1.Message}");

        // White: Pawn (3,-1) -> (2,0)
        var r2 = await SubmitMove(gameId, 3, -1, 2, 0);
        Assert.True(r2.Success, $"White move failed: {r2.Message}");

        // Red: Pawn (-1,2) -> (0,1)
        var r3 = await SubmitMove(gameId, -1, 2, 0, 1);
        Assert.True(r3.Success, $"Red move failed: {r3.Message}");

        // Should be Blue's turn again
        var status = await GetStatus(gameId);
        Assert.Equal("Blue", status.Turn);
        Log($"Full cycle complete. Turn: {status.Turn}");
    }

    // ================================================================
    //  UNDO
    // ================================================================

    [Fact]
    public async Task Undo_RevertsMoveAndTurn()
    {
        var gameId = NewGameId();
        await _client.PostAsync($"/Game/create?gameId={gameId}", null);

        await SubmitMove(gameId, -1, -1, -1, 0); // Blue move

        var response = await _client.PostAsync($"/Game/undo?gameId={gameId}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var status = await GetStatus(gameId);
        Assert.Equal("Blue", status.Turn);

        var board = await GetBoard(gameId);
        Assert.True(board.Any(p => p.Q == -1 && p.R == -1 && p.Piece == "Pawn"),
            "Pawn should be back at (-1,-1) after undo");
    }

    [Fact]
    public async Task Undo_AtStart_ReturnsBadRequest()
    {
        var gameId = NewGameId();
        await _client.PostAsync($"/Game/create?gameId={gameId}", null);

        var response = await _client.PostAsync($"/Game/undo?gameId={gameId}", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ================================================================
    //  EXPORT
    // ================================================================

    [Fact]
    public async Task Export_ReturnsFullGameState()
    {
        var gameId = NewGameId();
        await _client.PostAsync($"/Game/create?gameId={gameId}", null);

        var response = await _client.GetAsync($"/Game/export?gameId={gameId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("pieces", body);
        Assert.Contains("currentTurn", body);
        Log($"Export (first 200 chars): {body[..Math.Min(200, body.Length)]}");
    }

    // ================================================================
    //  GAME ISOLATION
    // ================================================================

    [Fact]
    public async Task TwoGames_IndependentState()
    {
        var gameA = NewGameId();
        var gameB = NewGameId();

        await _client.PostAsync($"/Game/create?gameId={gameA}", null);
        await _client.PostAsync($"/Game/create?gameId={gameB}", null);

        // Move in game A
        await SubmitMove(gameA, -1, -1, -1, 0);

        // Game A should be White's turn, Game B still Blue's
        var statusA = await GetStatus(gameA);
        var statusB = await GetStatus(gameB);

        Assert.Equal("White", statusA.Turn);
        Assert.Equal("Blue", statusB.Turn);
    }

    // ================================================================
    //  HELPERS
    // ================================================================

    private async Task<StatusResponse> GetStatus(string gameId)
    {
        var response = await _client.GetAsync($"/Game/status?gameId={gameId}");
        return await ParseJson<StatusResponse>(response);
    }

    private async Task<List<PieceResponse>> GetBoard(string gameId)
    {
        var response = await _client.GetAsync($"/Game/board?gameId={gameId}");
        return await ParseJson<List<PieceResponse>>(response);
    }

    private async Task<MoveResult> SubmitMove(string gameId, int q1, int r1, int q2, int r2)
    {
        var response = await _client.PostAsync(
            $"/Game/move?gameId={gameId}&q1={q1}&r1={r1}&q2={q2}&r2={r2}", null);

        var body = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<MoveResult>(body)
            ?? new MoveResult { Success = false, Message = "Failed to parse response" };
    }

    private async Task<T> ParseJson<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<T>(body)!;
    }

    // Response DTOs
    private class StatusResponse
    {
        public string Turn { get; set; } = "";
        public string State { get; set; } = "";
        public string Message { get; set; } = "";
    }

    private class PieceResponse
    {
        public string Piece { get; set; } = "";
        public string Color { get; set; } = "";
        public int Q { get; set; }
        public int R { get; set; }
    }

    private class MoveResponse
    {
        public int Q { get; set; }
        public int R { get; set; }
    }

    private class MoveResult
    {
        public bool Success { get; set; }
        public string NewTurn { get; set; } = "";
        public string Message { get; set; } = "";
    }
}
