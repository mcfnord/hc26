using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using HexC.Engine;
using HexC.AI;

namespace HexC.Simulator
{
    class Program
    {
        static string BaseUrl = "http://localhost:5235";
        static string GameId = "SimMatch_01";

        static void Main(string[] args)
        {
            Console.WriteLine("=== HEX CHESS SIMULATOR ===");
            Console.WriteLine($"Connecting to {BaseUrl}...");

            using (var client = new HttpClient())
            {
                // 1. Create the Game
                try
                {
                    var content = new StringContent("", Encoding.UTF8, "application/json");
                    _ = client.PostAsync($"{BaseUrl}/Game/create?gameId={GameId}", content).Result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"CRITICAL ERROR: Is the server running? {ex.Message}");
                    return;
                }

                // 2. The Game Loop
                bool gameRunning = true;
                while (gameRunning)
                {
                    try 
                    {
                        // A. GET STATUS & BOARD
                        var statusJson = client.GetStringAsync($"{BaseUrl}/Game/status?gameId={GameId}").Result;
                        var status = JsonConvert.DeserializeObject<ApiStatus>(statusJson);

                        var boardJson = client.GetStringAsync($"{BaseUrl}/Game/board?gameId={GameId}").Result;
                        var pieces = JsonConvert.DeserializeObject<List<ApiPiece>>(boardJson);
                        
                        // Reconstruct local board for visualization
                        Board localBoard = new Board();
                        foreach (var p in pieces)
                        {
                            if (Enum.TryParse<PiecesEnum>(p.Piece, out var type) && 
                                Enum.TryParse<ColorsEnum>(p.Color, out var color))
                            {
                                localBoard.Add(new PlacedPiece(type, color, p.Q, p.R));
                            }
                        }

                        // B. VISUALIZE FIRST
                        Console.Clear();
                        Visualizer.ShowBoard(localBoard, null, $"Turn: {status.Turn} | {status.Message}");

                        // C. CHECK GAME OVER (After showing the board!)
                        if (status.State == "Finished")
                        {
                            Console.WriteLine("\n" + new string('*', 40));
                            Console.WriteLine($"GAME OVER: {status.Message}");
                            Console.WriteLine(new string('*', 40));
                            Console.WriteLine("Press Enter to exit...");
                            Console.ReadLine();
                            gameRunning = false;
                            break;
                        }

                        // D. INTERACTIVE PAUSE
                        Console.WriteLine("\nPress [ENTER] for next move (or Q to Quit)...");
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.Q) break;
                        if (key.Key != ConsoleKey.Enter) continue;

                        Console.WriteLine("Thinking...");

                        // E. EXECUTE AI TURN
                        if (Enum.TryParse<ColorsEnum>(status.Turn, true, out var turnColor))
                        {
                            var bot = new BasicBot(turnColor);
                            var move = bot.PickMove(localBoard);

                            if (move != null)
                            {
                                Console.WriteLine($"AI {turnColor} plays: {move.Description}");
                                
                                var url = $"{BaseUrl}/Game/move?gameId={GameId}&q1={move.Q1}&r1={move.R1}&q2={move.Q2}&r2={move.R2}";
                                var moveContent = new StringContent("", Encoding.UTF8, "application/json");
                                var moveResult = client.PostAsync(url, moveContent).Result;

                                if (!moveResult.IsSuccessStatusCode)
                                {
                                    Console.WriteLine($"SERVER REJECTED MOVE: {moveResult.ReasonPhrase}");
                                    Thread.Sleep(2000); 
                                }
                            }
                            else
                            {
                                Console.WriteLine($"AI {turnColor} has NO MOVES. Passing turn...");
                                Thread.Sleep(1000);
                            }
                        }
                    }
                    catch (Exception loopEx)
                    {
                        Console.WriteLine($"Loop Error: {loopEx.Message}");
                        Thread.Sleep(1000);
                    }
                }
            }
        }

        private class ApiStatus { public string Turn { get; set; } public string State { get; set; } public string Message { get; set; } }
        private class ApiPiece { public string Piece { get; set; } public string Color { get; set; } public int Q { get; set; } public int R { get; set; } }
    }

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
                    PlacedPiece p = b.AnyoneThere(spot);
                    if (p == null)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write("·");
                    }
                    else
                    {
                        switch (p.Color) {
                            case ColorsEnum.Blue: Console.ForegroundColor = ConsoleColor.Cyan; break;
                            case ColorsEnum.White: Console.ForegroundColor = ConsoleColor.White; break;
                            case ColorsEnum.Red: Console.ForegroundColor = ConsoleColor.Red; break;
                        }
                        Console.Write(p.ToChar());
                    }
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.Write(" ");
                }
                Console.WriteLine();
            }
            Console.ResetColor();
        }
    }
}