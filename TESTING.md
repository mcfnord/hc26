# HexChess Testing Architecture

## Quick Start

    # Run all tests (engine + API integration)
    dotnet test HexC.Tests/

    # Run tests then start server (blocks server if tests fail)
    chmod +x test-and-run.sh
    ./test-and-run.sh

    # Test only (no server)
    ./test-and-run.sh --test

## Test Organization

### EngineTests.cs — Pure Logic (Layer 1)
Fast, isolated tests that exercise the game engine directly. No HTTP, no server.
Uses `BoardBuilder` to construct specific board states and `Game` to validate rules.

Coverage: board coordinates, all piece movement, phalanx, portal, reincarnation,
diddilydoo, check/checkmate, turn order, undo, valid moves, board setup,
and especially: Any bug detected by anyone that required a code change, if possible.

### ApiIntegrationTests.cs — HTTP Pipeline (Layer 2)
Uses `WebApplicationFactory<Program>` to spin up the real ASP.NET Core pipeline
in-process. Tests the full request->controller->engine->response chain.

Coverage: game creation, status, board, valid moves, move submission, full turn
cycle, undo via API, export, game isolation.

### TestHelpers.cs — Shared Utilities
- `BoardBuilder`: Fluent API for setting up test boards
- `BoardDiagnostics`: Human-readable board descriptions for failure messages

## File Summary

| File | Action |
|------|--------|
| `HexC.Tests/hc-tests.cs` | DELETE |
| `HexC.Tests/HexC.Tests.csproj` | REPLACE |
| `HexC.Tests/TestHelpers.cs` | CREATE |
| `HexC.Tests/EngineTests.cs` | CREATE |
| `HexC.Tests/HexChessWebFactory.cs` | CREATE |
| `HexC.Tests/ApiIntegrationTests.cs` | CREATE |
| `HexC.Server/Program.cs` | APPEND one line |
| `test-and-run.sh` | CREATE |
| `TESTING.md` | CREATE |
