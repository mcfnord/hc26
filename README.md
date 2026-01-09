# Hexagonal Chess Online (HexC)

**HexC** is a three-player hexagonal chess engine and server built on **ASP.NET Core** and **C#**. It features unique mechanics including a central Portal, unit reincarnation, "Phalanx" defensive formations, and the "Diddilydoo" maneuver.

This repository contains:
* **HexC.Engine**: The core C# game logic, rule enforcement, and board state management.
* **HexC.Server**: An ASP.NET Core Web API that hosts matches and manages game state.
* **HexC.Tests**: A comprehensive xUnit test suite including local logic tests and integration tests.

## 🔷 Rules of the Game

### Players & Turn Order
The game is played by three factions on a hexagonal board. Turn order is strictly enforced:
1.  **Blue** (Cyan in visualizers)
2.  **White**
3.  **Red**

### Winning Conditions
The game ends when a victory condition is met. There are two ways to win:

#### 1. Ascension (Portal Victory)
* If a player moves their **King** onto the central **Portal** (coordinate `0,0`), they instantly win the game.

#### 2. Political Checkmate (The "Third Man" Rule)
Victory is not determined when a move is made, but **at the start of the victim's turn**.
* **The Trigger:** A player loses if they **start their turn** in a state of Checkmate (King is attacked and cannot escape).
* **The Politics:** Because the victory is delayed until the victim's turn, there might be a "Third Player" (the player moving between the Attacker and the Victim) with power to intervene.
    * *Example:* **Blue** checkmates **Red**, and **White** takes their turn next. White can choose to end the checkmate state (saving Red) or ignore it (letting Red die and Blue win).
* **The Winner:** The winner is the player currently attacking the victim.
    * *Tie-Breaker:* If two players are attacking the victim simultaneously, the winner is the player who first put the victim into checkmate.

---

## ⚡ Special Mechanics

### 🌀 The Portal & Reincarnation
The center hex (`0,0`) is the **Portal**. It has special properties regarding capture and piece recovery:

* **King Victory:** As stated above, a King landing here wins the game.
* **The Void:** Non-King pieces cannot survive in the Portal. If they move there (or are pushed there), they vanish from the board.
* **Reincarnation:** This is the primary comeback mechanic.
    * **Trigger:** If you capture an enemy piece adjacent to (or inside) the Portal...
    * **Condition:** ...AND you have previously lost a piece of that same type (e.g., you capture a Pawn, and you have a Pawn currently in your "graveyard")...
    * **Effect:** ...One of your lost pieces of that type is immediately **resurrected** at the Portal (`0,0`), but you lose this piece if you don't move it out of the portal on your next turn.

### 🛡️ The Phalanx (Pawn Invincibility)
Pawns can form a defensive line known as a **Phalanx**.
* **Rule:** A Pawn is immune to capture if it is adjacent to **two or more** other Pawns of the same color.
* **Strategy:** Keep your pawns in tight clusters to push forward safely without fear of being picked off by Castles or Elephants.

### 🔄 The Diddilydoo (King-Queen Swap)
The "Diddilydoo" is a special two-stage maneuver, similar to Castling but more flexible.
* **Requirement:** Your King and Queen must be adjacent to each other.
* **Action:** You may signal the King to move onto the Queen's square (or vice versa).
* **Effect:** The two pieces instantly swap positions.
* **Bonus:** This swap does **not** end your turn. You immediately get to take your **Main Move**.

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
A robust test suite using a custom `LocalTestRunner` and `IntegrationTestRunner`.
* **Local Tests:** Inject specific board states (like "God Mode") to verify edge cases (e.g., Phalanx protection, Reincarnation logic).
* **Integration Tests:** Spin up a real instance of the API and interact with it via HTTP to ensure the server pipeline works correctly.

## 🚀 Getting Started

### Prerequisites
* .NET SDK (6.0 or later recommended)

### Running the Server
```powershell
cd HexC.Server
dotnet run
