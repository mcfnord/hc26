# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

HexC: a three-player (Blue, White, Red) hexagonal chess game. It has a C# engine, an ASP.NET Core server (.NET 8) that serves a single-page web UI, a random-ish AI bot and a console simulator. The game rules (Portal/Ascension, delayed Checkmate, Reincarnation, Mob pawn immunity, Diddilydoo King–Queen swap) are in `README.md`. Read it before changing rule logic, because several rules are not standard chess.

## Commands

```bash
dotnet build HexChess.sln
dotnet test HexC.Tests/                                   # all tests (see UI caveat below)
dotnet test HexC.Tests/ --filter "Category!=UI"           # skip Playwright browser tests
dotnet test HexC.Tests/ --filter "FullyQualifiedName~EngineTests.SomeTestName"   # single test
dotnet test HexC.Tests/ --filter "FullyQualifiedName~ApiIntegrationTests"        # one class
dotnet run --project HexC.Server                          # serves on http://localhost:5235 (UI at /)
dotnet run --project HexC.Simulator                       # needs the server already running on :5235
./test-and-run.sh [--test|--serve]                        # restore+build+test, then serve if green
```

`UiIntegrationTests` (`[Trait("Category","UI")]`) use Playwright against a **live** server at `localhost:5235`. They need a one-time `pwsh HexC.Tests/bin/Debug/net8.0/playwright.ps1 install` and a running `HexC.Server`. Without both, they fail, so exclude them with the filter above for normal runs.

## Architecture

**HexC.Engine/hc-engine.cs** holds all rules in one file.
- Axial hex coordinates `(q, r)`, radius 5 (`BoardLocation.IsValidLocation`). The Portal is `(0,0)`.
- `Board` is a sparse `List<PlacedPiece>`. `SidelinedPieces` (the graveyard, used for reincarnation) isn't stored. It's computed as the full starting set minus the pieces on the board.
- Move generation returns *outcomes as event lists*. `Board.WhatCanICauseWithDoo(piece)` returns `List<List<PieceEvent>>`, where each inner list is a set of `Add`/`Remove` events (move, capture, reincarnation at the portal, vanishing into the portal void). Callers (`Game.GetValidMoves`, `BasicBot`) find the destination by locating the `Add` event for the moving piece's type. If there's no such event, the piece went into the portal.
- `Game` is the state machine. Turn order is **Blue → White → Red**, and Blue moves first. `MainMovePending` tracks the Diddilydoo: a swap doesn't end the turn, and it can be swapped back. `SubmitMove` pushes a `GameSnapshot` onto an undo stack (`TakeBack`) and appends to `Timeline` (the replay/review data). Checkmate is evaluated at the **start of the victim's turn** (`AdvanceTurn` → `CheckVictoryAtStartOfTurn`), including "Priority Checkmate" credit when a mate survives a third player's turn.
- `SubmitMove` returns `void`. It reports failure only through `StatusMessage`, leaving state unchanged. The server infers success by comparing `CurrentTurn`, `State` and `MainMovePending` before and after the call (`gamecontroller.cs`). Keep that contract in mind if you change move semantics.

**HexC.Server**
- `GameStore` is a static in-memory `ConcurrentDictionary`, keyed by the lowercased game ID, with the creator's original casing kept separately (`canonicalId`). Game IDs must be letters only. Nothing is persisted.
- `GameController` (`/Game/*`) is a thin JSON projection over `Game`/`Board`. Endpoints: `create`, `status`, `board`, `validMoves`, `threats` (attack paths used by the UI to explain illegal moves), `move`, `ai-move`, `undo`, `timeline`, `export`, `sidelined`, `canonicalId`. Coordinates travel as query params (`q1,r1,q2,r2`).
- `wwwroot/index.html` is the whole client: one file with inline JS/SVG, anime.js from a CDN, and API base derived from `window.location.pathname` + `Game`. Playwright tests depend on its element IDs (`#gameIdInput`, `#turn-indicator`, `#pieces-group`, `#diddilydoo-toggle`).
- `public partial class Program {}` at the end of `Program.cs` exists so `WebApplicationFactory<Program>` works. Keep it.

**HexC.AI/BasicBot.cs**: priority order is King-to-portal win, then a random capture, then a random legal move. The server's `ai-move` endpoint and the Simulator both use it.

**HexC.Tests** (xUnit + FluentAssertions)
- `EngineTests`, `CheckmateDetectionTests` and `HighlightingTests` test the engine directly. Build positions with `BoardBuilder.Create().WithKing(...)...BuildGame(turn)` (in `TestHelpers.cs`). Use `BoardDiagnostics.Describe(board)` in assertion messages.
- `ApiIntegrationTests` runs the real pipeline in-process through `HexChessWebFactory`. Because `GameStore` is static and shared, use unique game IDs per test.
- Project convention (from `TESTING.md`): every bug that needed a code fix should get a regression test in `EngineTests` when feasible.

## How the operator wants to work

- **Claude's memory feature is PROHIBITED in this project.** Never write to, read from, or create files under `~/.claude/projects/*/memory/` or any `MEMORY.md`. If you notice such a file exists for this project, delete it and tell the operator. Everything worth remembering goes in `.md` files checked into this repo: this file (how to work), `PLAN.md` (the vision and decisions), `TODO.md` (small open items).
- **The vision lives in `PLAN.md`.** Read it before proposing work. When a decision changes the vision, record it there with the date.
- **Small steps.** Propose the next smallest step that can be deployed and confirmed on https://johns.living, do it, deploy it, and stop for confirmation before continuing. Don't chain phases.
- **Rollback is a feature.** Every deploy must be revertible with `deploy/revert.sh`.
