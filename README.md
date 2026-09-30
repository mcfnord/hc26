# Hexagonal Chess Online (HexC)

**HexC** is a three-player hexagonal chess engine and server built on **ASP.NET Core** and **C#**. It features unique mechanics including a central Portal, unit reincarnation, "Mob" defensive formations, and the "Diddilydoo" maneuver.

This repository contains:
* **HexC.Engine**: The core C# game logic, rule enforcement, and board state management.
* **HexC.Server**: An ASP.NET Core Web API that hosts matches and manages game state.
* **HexC.Tests**: A comprehensive xUnit test suite including local logic tests and integration tests.

## 🔷 Rules of the Game

HexC is chess for three, on a hexagonal board, with a Portal in the middle. If you know
chess you know most of it. The differences are marked **(not chess)**.

### The board and the players
* The board is a hexagon of hexagons, 6 hexes on each side (91 hexes in all). The centre hex is the **Portal**.
* Three players: **Blue**, **White** and **Red**. Each starts in their own corner with 10 pieces:
  a King, a Queen, 2 Castles, 3 Elephants and 3 Pawns.
* **Blue moves first**, then White, then Red, and round again.
* A move is one piece to one hex. You may capture a piece of *either* other colour by landing on it.
* **Kings are never captured** and you may never end a move with your own King under attack
  (attacked by *either* opponent). The game is decided by checkmate or by reaching the Portal.

### How the pieces move
A hex has six *neighbours* (sharing an edge) and six *diagonals* (the hexes two steps away
that touch two of your neighbours). Each diagonal is reached through a **gate**: the pair of
neighbours it touches. A gate is **open** if at least one of those two hexes is empty.

| Piece | Movement |
|---|---|
| **King** | One step to any neighbour. |
| **Castle** | Slides any distance in a straight line (the six neighbour directions), stopping at the first piece. Captures it if it's an enemy. |
| **Queen** | Slides like a Castle **or** makes a **three-step diagonal walk (not chess)**: exactly three diagonal steps, turning as it likes, each through an open gate, over empty hexes, finishing on an empty hex or an enemy. It may not finish where it started. |
| **Elephant** | Jumps like a chess knight, to any of the 12 hexes that are "two out and one across" (**not chess**). Nothing in between matters. |
| **Pawn** | Moves **one step to any empty neighbour, in any direction** (**not chess**: no forward direction and no promotion). Captures **diagonally**: one diagonal step onto an enemy, through an open gate. |

### Check and checkmate
* Your King is **in check** when a piece of either other colour could take its hex.
* You may never move so that your own King is in check afterwards. If every legal move
  (including a Diddilydoo) leaves your King in check, you are in **checkmate**.
* **Checkmate is judged at the start of the victim's turn, not when the move is made (not chess).**
  A player who *begins* their turn in checkmate loses, and the game is over.
* **Who wins:** the player whose move created the checkmate. Because the game only ends when the
  victim's turn arrives, the *third* player moves in between and may spoil the mate, capture the
  attacker, or leave it alone. If they leave it, the original attacker wins by **Priority Checkmate**.

### 🌀 The Portal (not chess)
The centre hex is the Portal. It behaves differently from every other hex.

* **Ascension:** a King that moves onto the Portal **wins the game instantly**, whether the Portal is
  empty or the King captures an enemy standing there. The usual rule applies: the King cannot move onto
  a hex that an opponent attacks, so a *defended* Portal is out of reach.
* **Only Kings may enter an empty Portal.** Other pieces cannot move onto it, and Castles and Queens
  cannot slide *through* it. An empty Portal blocks a straight line like a wall.
* **Attacking into the Portal:** any piece may capture an enemy that is standing on the Portal.
  For every piece but the King, both then vanish: the victim and the attacker. A King survives and wins.
* **Your own piece on the Portal** blocks your other pieces as usual. Enemies can attack it there.

### ♻️ Reincarnation (not chess)
Pieces you've lost can come back.

* **When:** the moment you capture an enemy piece...
* **If:** ...your graveyard already holds a piece of the *same type* (you capture a Pawn and you had already lost a Pawn)...
* **and:** ...the Portal is empty (or is being emptied by this very capture).
* **Then:** one of your dead pieces of that type appears on the Portal at once. The capture and the reincarnation are one move.
* **Notes:** the graveyard is checked *before* your attacker leaves the board, so when you attack into
  the Portal the attacker itself never counts; you need *another* piece of the victim's type already dead.
  A reincarnated piece may itself be attacked on the Portal.
* **Move it or lose it:** a reincarnated piece must leave the Portal on your **next move**. If your next move
  is anything else, it vanishes again ("abandoned to the Portal").

### 🛡️ The Mob (not chess)
* Three Pawns of one colour standing in a **triangle** (each touching the other two) form a Mob.
* A Pawn in a Mob **cannot be captured**. Attackers simply may not land on it.
* A Pawn in a Mob also **cannot capture**. It may still step to an empty hex, which breaks the Mob
  (unless a reincarnated fourth Pawn keeps a triangle standing).
* The starting position places each player's three Pawns in a Mob.

### 🔄 The Diddilydoo (King–Queen swap, not chess)
* If your King and Queen are **adjacent**, you may swap their places at the start of your turn.
* The swap does **not** end your turn: you still make your ordinary move afterwards.
* You may swap back before making the move if you change your mind.
* You cannot swap into check, and the move that follows must not leave you in check.

### Review
Any game can be stepped through move by move with **Review**.

---

## 🛠️ Technical Architecture

### HexC.Engine
A standalone C# library containing the game rules.
* **`Board`**: Manages a sparse list of `PlacedPiece` objects. Uses Axial Coordinates (`q`, `r`).
* **`Game`**: The state machine. Manages turn order (`ColorsEnum`), victory checks, and the "MainMovePending" state for the Diddilydoo.
* **Validation**: All moves are validated server-side. Invalid moves return descriptive error messages without altering the board state.

### HexC.Server
A lightweight REST API serving the game.
* **`POST /Game/create`**: Initializes a new match with the standard 3-player setup.
* **`POST /Game/move`**: Accepts `q1, r1` (origin) and `q2, r2` (destination). Handles complex logic like Swaps and Reincarnation internally.
* **`GET /Game/board`**: Returns the current list of pieces for rendering.

### HexC.Tests
xUnit tests. `EngineTests`, `CheckmateDetectionTests` and `HighlightingTests` build specific positions
(`BoardBuilder`) and check the engine directly; `ApiIntegrationTests` runs the server in-process;
`UiIntegrationTests` drives the page with Playwright against a running server. See `TESTING.md` and `CLAUDE.md`.

## 🚀 Getting Started

### Prerequisites
* .NET SDK 8.0

### Running the Server
```bash
dotnet run --project HexC.Server      # http://localhost:5235
