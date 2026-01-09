#nullable disable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using HexC.Engine;

namespace HexC.Tests
{
    // --- 1. VISUALIZATION HELPER ---
    public static class Visualizer
    {
        public static void ShowBoard(Board b, BoardLocation highlight = null, string caption = "")
        {
            Console.WriteLine($"--- {caption} ---");
            int[,] Lines = { { 6,  0, -5 }, { 7, -1, -4 }, { 8, -2, -3 }, { 9, -3, -2 },
                             {10, -4, -1 }, {11, -5,  0 }, {10, -5,  1 }, { 9, -5,  2 },
                             { 8, -5,  3 }, { 7, -5,  4 }, { 6, -5,  5 } };

            for (int iLine = 0; iLine < Lines.GetLength(0); iLine++)
            {
                Console.ResetColor();
                Console.Write("             ".Substring(0, 11 - Lines[iLine, 0]));

                for (int iPos = 0; iPos < Lines[iLine, 0]; iPos++)
                {
                    BoardLocation spot = new BoardLocation(Lines[iLine, 1] + iPos, Lines[iLine, 2]);

                    if (spot.IsPortal) Console.BackgroundColor = ConsoleColor.DarkCyan;
                    if (highlight != null && BoardLocation.IsSameLocation(highlight, spot)) Console.BackgroundColor = ConsoleColor.Magenta;

                    PlacedPiece p = b.AnyoneThere(spot);
                    if (p == null)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write("·");
                    }
                    else
                    {
                        SetPieceColor(p.Color);
                        Console.Write(p.ToChar());
                    }
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.Write(" ");
                }
                Console.WriteLine();
            }
            Console.ResetColor();
        }

        private static void SetPieceColor(ColorsEnum color)
        {
            switch (color)
            {
                // LOGIC is Blue, VISUAL is Cyan (Readable Blue)
                case ColorsEnum.Blue: Console.ForegroundColor = ConsoleColor.Cyan; break;
                
                case ColorsEnum.White: Console.ForegroundColor = ConsoleColor.White; break;
                case ColorsEnum.Red: Console.ForegroundColor = ConsoleColor.Red; break;
            }
        }
    }

    // --- 2. REMOTE GAME PROXY ---
    public class RemoteGame
    {
        private HttpClient _client;
        private string _baseUrl;
        public string GameId { get; private set; }
        public Board LocalBoardCopy { get; private set; }
        public string StatusMessage { get; private set; }
        public string CurrentTurn { get; private set; }

        public RemoteGame(string gameId)
        {
            _baseUrl = "http://localhost:5235"; 
            _client = new HttpClient();
            GameId = gameId;
            LocalBoardCopy = new Board();
        }

        public void Create()
        {
            try
            {
                var content = new StringContent("", Encoding.UTF8, "application/json");
                var result = _client.PostAsync($"{_baseUrl}/Game/create?gameId={GameId}", content).Result;
                RefreshState();
            }
            catch (Exception) { StatusMessage = "Connection Failed"; }
        }

        public void Move(int q1, int r1, int q2, int r2)
        {
            try
            {
                var content = new StringContent("", Encoding.UTF8, "application/json");
                var url = $"{_baseUrl}/Game/move?gameId={GameId}&q1={q1}&r1={r1}&q2={q2}&r2={r2}";
                var response = _client.PostAsync(url, content).Result;
                var json = response.Content.ReadAsStringAsync().Result;

                if (!response.IsSuccessStatusCode)
                {
                    var error = JsonConvert.DeserializeObject<ApiStatus>(json);
                    StatusMessage = $"ERROR: {error?.Message ?? "Unknown"}";
                }
                RefreshState();
            }
            catch (Exception ex) { StatusMessage = $"Connection Failed: {ex.Message}"; }
        }

        private void RefreshState()
        {
            var statusJson = _client.GetStringAsync($"{_baseUrl}/Game/status?gameId={GameId}").Result;
            var status = JsonConvert.DeserializeObject<ApiStatus>(statusJson);
            
            StatusMessage = status.Message;
            CurrentTurn = status.Turn;

            var boardJson = _client.GetStringAsync($"{_baseUrl}/Game/board?gameId={GameId}").Result;
            var pieces = JsonConvert.DeserializeObject<List<ApiPiece>>(boardJson);

            LocalBoardCopy = new Board();
            foreach (var p in pieces)
            {
                if (Enum.TryParse<PiecesEnum>(p.Piece, out var type) && 
                    Enum.TryParse<ColorsEnum>(p.Color, out var color))
                {
                    LocalBoardCopy.Add(new PlacedPiece(type, color, p.Q, p.R));
                }
            }
        }

        private class ApiStatus { public string Turn { get; set; } public string State { get; set; } public string Message { get; set; } }
        private class ApiPiece { public string Piece { get; set; } public string Color { get; set; } public int Q { get; set; } public int R { get; set; } }
    }

    // --- 3. TEST RUNNERS ---

    public static class LocalTestRunner
    {
        // Helper: Asks the ENGINE to perform the move legitimately (for basic moves)
        public static void AttemptLegitimateMove(Board b, int q1, int r1, int q2, int r2)
        {
            var piece = b.AnyoneThere(new BoardLocation(q1, r1));
            if (piece == null) 
            {
                Console.WriteLine($"[Move Failed] No piece at {q1},{r1}");
                return;
            }

            var options = b.WhatCanICauseWithDoo(piece);
            
            foreach(var eventSet in options)
            {
                bool isMatch = false;
                foreach(var evt in eventSet)
                {
                    if (evt.EventType == EventTypeEnum.Add && 
                        evt.Regarding.Location.Q == q2 && 
                        evt.Regarding.Location.R == r2 &&
                        evt.Regarding.PieceType == piece.PieceType)
                    {
                        isMatch = true;
                        break;
                    }
                }

                if (isMatch)
                {
                    foreach(var evt in eventSet)
                    {
                        if (evt.EventType == EventTypeEnum.Remove) b.Remove(evt.Regarding);
                        if (evt.EventType == EventTypeEnum.Add) b.Add(evt.Regarding);
                    }
                    Console.WriteLine($"[Move Success] {piece.Color} {piece.PieceType} -> {q2},{r2}");
                    return;
                }
            }
            Console.WriteLine($"[Move Illegal] Engine rules rejected {piece.Color} {piece.PieceType} -> {q2},{r2}");
        }

        public static void Run(string testName, Action<Board> setup, Action<Board> action, Func<Board, bool> assertion)
        {
            Console.WriteLine("\n" + new string('-', 60));
            Console.WriteLine($"TEST (Local): {testName}");
            Board b = new Board();
            try 
            {
                setup(b);
                Visualizer.ShowBoard(b, null, "BEFORE"); 
                action(b);
                Visualizer.ShowBoard(b, null, "AFTER");

                if (assertion(b)) PrintPass(testName);
                else PrintFail(testName);
            }
            catch (Exception ex) { PrintError(testName, ex.Message); }
        }

        private static void PrintPass(string name) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine($"[PASS] {name}"); Console.ResetColor(); }
        private static void PrintFail(string name) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"[FAIL] {name}"); Console.ResetColor(); }
        private static void PrintError(string name, string msg) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"[ERR]  {name}: {msg}"); Console.ResetColor(); }
    }

    public static class IntegrationTestRunner
    {
        public static void Run(string testName, string gameId, Action<RemoteGame> action, Func<RemoteGame, bool> assertion)
        {
            Console.WriteLine("\n" + new string('-', 60));
            Console.WriteLine($"TEST (Integration): {testName}");
            try 
            {
                RemoteGame game = new RemoteGame(gameId);
                game.Create(); 
                
                if (game.StatusMessage == "Connection Failed")
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[ABORT] Server not reachable.");
                    Console.ResetColor();
                    return;
                }

                Visualizer.ShowBoard(game.LocalBoardCopy, null, "INITIAL SERVER STATE");
                Console.WriteLine($"Msg: {game.StatusMessage}");

                action(game);
                
                Visualizer.ShowBoard(game.LocalBoardCopy, null, "FINAL SERVER STATE");
                Console.WriteLine($"Msg: {game.StatusMessage}");

                if (assertion(game))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[PASS] {testName}");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[FAIL] {testName}");
                }
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[CRITICAL FAIL] {ex.Message}");
                Console.ResetColor();
            }
        }
    }

    // --- 4. MAIN PROGRAM ---
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== HEX CHESS TEST SUITE ===");

            // --- A: INTEGRATION TESTS ---
            IntegrationTestRunner.Run("Blue Pawn First Move", "TestGame_01",
                action: (game) => {
                    Console.WriteLine("Action: Server, move Blue Pawn from (-1,-1) to (-1,0).");
                    game.Move(-1, -1, -1, 0); 
                },
                assertion: (game) => {
                    return game.CurrentTurn == "White" 
                        && game.LocalBoardCopy.AnyoneThere(new BoardLocation(-1, 0))?.PieceType == PiecesEnum.Pawn;
                }
            );

            // --- B: LOCAL LOGIC TESTS ---
            
 // TEST 1: Portal Victory
            LocalTestRunner.Run("King Portal Victory",
                setup: (b) => b.Add(new PlacedPiece(PiecesEnum.King, ColorsEnum.Red, 0, 1)),
                action: (b) => {
                    Console.WriteLine("Action: Move King (0,1) -> (0,0)");
                    Game g = new Game(); 
                    // FIX: Load State (Board + Turn) together
                    g.LoadMatchState(b, ColorsEnum.Red); 
                    g.SubmitMove(0, 1, 0, 0);
                },
                assertion: (b) => b.AnyoneThere(new BoardLocation(0, 0))?.PieceType == PiecesEnum.King
            );

            // TEST 2: Phalanx Protection (Blue is default, so standard init is fine, but we can be explicit)
            LocalTestRunner.Run("Pawn Phalanx Protection",
                setup: (b) => {
                    b.Add(new PlacedPiece(PiecesEnum.Pawn, ColorsEnum.Blue, 1, -1)); 
                    b.Add(new PlacedPiece(PiecesEnum.Pawn, ColorsEnum.Blue, 0, -1));
                    b.Add(new PlacedPiece(PiecesEnum.Pawn, ColorsEnum.Blue, 1, -2)); 
                    b.Add(new PlacedPiece(PiecesEnum.Castle, ColorsEnum.White, 1, -4));
                },
                action: (b) => { 
                    Console.WriteLine("Action: Attempting Castle Attack on Protected Pawn (1,-2)...");
                    Game g = new Game(); 
                    g.LoadMatchState(b, ColorsEnum.White); // Attackers turn
                    g.SubmitMove(1, -4, 1, -2);
                },
                assertion: (b) => {
                    bool attackerStayed = b.AnyoneThere(new BoardLocation(1, -4))?.PieceType == PiecesEnum.Castle;
                    bool victimSurvived = b.AnyoneThere(new BoardLocation(1, -2))?.PieceType == PiecesEnum.Pawn;
                    return attackerStayed && victimSurvived;
                }
            );

            // TEST 3: Portal Reincarnation (Success)
            LocalTestRunner.Run("Portal Reincarnation (Success)",
                setup: (b) => {
                    b.Add(new PlacedPiece(PiecesEnum.Pawn, ColorsEnum.Red, 1, 0)); // Victim
                    b.Add(new PlacedPiece(PiecesEnum.Castle, ColorsEnum.White, 2, -1)); // Attacker
                },
                action: (b) => {
                    Console.WriteLine("Action: Attacking Red Pawn at (1,0)...");
                    Game g = new Game(); 
                    g.LoadMatchState(b, ColorsEnum.White); // White's turn
                    g.SubmitMove(2, -1, 1, 0);
                },
                assertion: (b) => {
                    var p = b.AnyoneThere(new BoardLocation(0,0));
                    if (p != null) Console.WriteLine($"Result: {p.Color} {p.PieceType} spawned in Portal.");
                    return p != null && p.Color == ColorsEnum.White && p.PieceType == PiecesEnum.Pawn;
                }
            );

            // TEST 4: Portal Reincarnation (Fail - No Sidelined Pieces)
            LocalTestRunner.Run("Reincarnation Fails (Empty Sidelines)",
                setup: (b) => {
                    b.Add(new PlacedPiece(PiecesEnum.Pawn, ColorsEnum.White, 3, -2));
                    b.Add(new PlacedPiece(PiecesEnum.Pawn, ColorsEnum.White, 3, -1));
                    b.Add(new PlacedPiece(PiecesEnum.Pawn, ColorsEnum.White, 4, -2)); 

                    b.Add(new PlacedPiece(PiecesEnum.Pawn, ColorsEnum.Red, 1, 0)); // Victim
                    b.Add(new PlacedPiece(PiecesEnum.Castle, ColorsEnum.White, 2, -1)); // Attacker
                },
                action: (b) => {
                    Console.WriteLine("Action: Attacking Red Pawn (No reserves)...");
                    Game g = new Game(); 
                    g.LoadMatchState(b, ColorsEnum.White); // White's turn
                    g.SubmitMove(2, -1, 1, 0);
                },
                assertion: (b) => {
                    var victim = b.AnyoneThere(new BoardLocation(1,0));
                    var attacker = b.AnyoneThere(new BoardLocation(1,0)); 
                    var portal = b.AnyoneThere(new BoardLocation(0,0));

                    bool attackSuccess = (victim == null || victim.PieceType != PiecesEnum.Pawn) && 
                                         (attacker != null && attacker.PieceType == PiecesEnum.Castle);
                    bool noSpawn = (portal == null);

                    if (!attackSuccess) Console.WriteLine("Result: Attack FAILED (Move didn't happen).");
                    else if (noSpawn) Console.WriteLine("Result: Attack Succeeded. Portal Empty (Correct).");
                    else Console.WriteLine("Result: Portal Occupied (Fail).");

                    return attackSuccess && noSpawn;
                }
            );

            // TEST 5: The Diddilydoo (King-Queen Swap)
            LocalTestRunner.Run("The Diddilydoo (King-Queen Swap)",
                setup: (b) => {
                    b.Add(new PlacedPiece(PiecesEnum.King, ColorsEnum.Blue, 3, -3));
                    b.Add(new PlacedPiece(PiecesEnum.Queen, ColorsEnum.Blue, 3, -2));
                },
                action: (b) => {
                    Game g = new Game();
                    g.LoadMatchState(b, ColorsEnum.Blue); // Blue's turn
                    
                    Console.WriteLine("Action: King attempts to Swap with Queen at (3,-2)...");
                    g.SubmitMove(3, -3, 3, -2);
                    
                    if (g.MainMovePending) Console.WriteLine("Result: Swap Success! Main move pending.");
                    else Console.WriteLine($"Result: Swap Failed. Msg: {g.StatusMessage}");
                },
                assertion: (b) => {
                    var kingSpot = b.AnyoneThere(new BoardLocation(3, -2));
                    var queenSpot = b.AnyoneThere(new BoardLocation(3, -3));
                    return kingSpot?.PieceType == PiecesEnum.King && queenSpot?.PieceType == PiecesEnum.Queen;
                }
            );

            // TEST 6: Diddilydoo into Portal Victory
            LocalTestRunner.Run("Diddilydoo into Portal Victory",
                setup: (b) => {
                    // SETUP:
                    // Portal is at (0,0).
                    // Place Queen at (1,0) - Adjacent to Portal.
                    // Place King at (2,0) - Adjacent to Queen.
                    b.Add(new PlacedPiece(PiecesEnum.Queen, ColorsEnum.Blue, 1, 0));
                    b.Add(new PlacedPiece(PiecesEnum.King, ColorsEnum.Blue, 2, 0));
                },
                action: (b) => {
                    Game g = new Game();
                    g.LoadMatchState(b, ColorsEnum.Blue); 
                    
                    Console.WriteLine("Action 1: Swap King (2,0) with Queen (1,0)...");
                    g.SubmitMove(2, 0, 1, 0); 
                    
                    if (g.MainMovePending) 
                        Console.WriteLine("   -> Swap OK. Bonus move active.");
                    else 
                        Console.WriteLine($"   -> Swap FAILED. Msg: {g.StatusMessage}");

                    Console.WriteLine("Action 2: Move King (now at 1,0) into Portal (0,0)...");
                    g.SubmitMove(1, 0, 0, 0);

                    if (g.State == GameStateEnum.Finished)
                        Console.WriteLine($"   -> GAME OVER. Msg: {g.StatusMessage}");
                },
                assertion: (b) => {
                    var portalContent = b.AnyoneThere(new BoardLocation(0, 0));
                    bool kingInPortal = portalContent != null && 
                                        portalContent.PieceType == PiecesEnum.King && 
                                        portalContent.Color == ColorsEnum.Blue;
                    
                    // Queen should now be back at (2,0) where the King started
                    var queenSpot = b.AnyoneThere(new BoardLocation(2, 0));
                    bool queenMoved = queenSpot != null && queenSpot.PieceType == PiecesEnum.Queen;

                    return kingInPortal && queenMoved;
                }
            );            

            Console.WriteLine("\nPress Enter to Exit.");
            Console.ReadLine();
        }
    }
}