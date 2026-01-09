using System;
using System.Collections.Generic;
using System.Linq;
using HexC.Engine;

namespace HexC.AI
{
    public class MoveCommand
    {
        public int Q1, R1, Q2, R2;
        public string Description;

        public MoveCommand(int q1, int r1, int q2, int r2, string desc)
        {
            Q1 = q1; R1 = r1; Q2 = q2; R2 = r2; Description = desc;
        }
    }

    public class BasicBot
    {
        private Random _rng = new Random();
        public ColorsEnum MyColor { get; private set; }

        public BasicBot(ColorsEnum color)
        {
            MyColor = color;
        }

        public MoveCommand PickMove(Board board)
        {
            // Filter to only my pieces
            var myPieces = board.PlacedPieces.Where(p => p.Color == MyColor).ToList();
            
            var allMoves = new List<MoveCommand>();
            var captureMoves = new List<MoveCommand>();
            var winningMoves = new List<MoveCommand>();

            foreach (var piece in myPieces)
            {
                // What moves can this piece make?
                var outcomes = board.WhatCanICauseWithDoo(piece);

                foreach (var outcome in outcomes)
                {
                    // Find the "Add" event that tells us the destination
                    var moveEvent = outcome.FirstOrDefault(e => e.EventType == EventTypeEnum.Add && e.Regarding.PieceType == piece.PieceType);
                    if (moveEvent == null) continue;

                    int q2 = moveEvent.Regarding.Location.Q;
                    int r2 = moveEvent.Regarding.Location.R;

                    // Did we capture an enemy?
                    var killEvent = outcome.FirstOrDefault(e => e.EventType == EventTypeEnum.Remove && e.Regarding.Color != MyColor);
                    bool isCapture = killEvent != null;

                    // Did we win? (King in Portal)
                    bool isWin = (piece.PieceType == PiecesEnum.King && q2 == 0 && r2 == 0);

                    string desc = $"{piece.PieceType} to {q2},{r2}";
                    var cmd = new MoveCommand(piece.Location.Q, piece.Location.R, q2, r2, desc);

                    if (isWin) winningMoves.Add(cmd);
                    else if (isCapture) captureMoves.Add(cmd);
                    else allMoves.Add(cmd);
                }
            }

            // PRIORITIES: Win -> Capture -> Random
            if (winningMoves.Any()) return winningMoves.First();
            if (captureMoves.Any()) return captureMoves[_rng.Next(captureMoves.Count)];
            if (allMoves.Any()) return allMoves[_rng.Next(allMoves.Count)];

            return null; // No legal moves found
        }
    }
}