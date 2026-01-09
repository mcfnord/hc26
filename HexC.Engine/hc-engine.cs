using System;
using System.Collections.Generic;
using System.Linq;

namespace HexC.Engine
{
    // --- BASIC DEFINITIONS ---
    public enum PiecesEnum { Pawn, Elephant, Castle, Queen, King }
    public enum ColorsEnum { White, Blue, Red }
    public enum EventTypeEnum { Add, Remove }
    public enum GameStateEnum { Active, Finished }

    // --- COORDINATES ---
    public class BoardLocation
    {
        private int q;
        private int r;
        public int Q { get { return q; } }
        public int R { get { return r; } }

        public BoardLocation(int q, int r) { this.q = q; this.r = r; }
        
        public bool IsValidLocation()
        {
            if (q > 5 || r > 5 || q < -5 || r < -5) return false;
            if (q + r > 5 || q + r < -5) return false;
            return true;
        }

        public static bool IsSameLocation(BoardLocation one, BoardLocation two)
        {
            if (one is null || two is null) return false;
            return one.Q == two.Q && one.R == two.R;
        }

        public bool IsPortal => (q == 0 && r == 0);
        public override string ToString() => $"{q},{r}";
    }

    public class BoardLocationList : List<BoardLocation>
    {
        public bool ContainsTheLocation(BoardLocation bToMatch)
        {
            foreach (BoardLocation bl in this)
                if (bl.Q == bToMatch.Q && bl.R == bToMatch.R) return true;
            return false;
        }
    }

    // --- PIECES ---
    public class Piece
    {
        public PiecesEnum PieceType { get; protected set; }
        public ColorsEnum Color { get; protected set; }
        public Piece(PiecesEnum pt, ColorsEnum c) { this.PieceType = pt; this.Color = c; }
        public char ToChar() { return PieceType.ToString()[0]; }
    }

    public class PlacedPiece : Piece
    {
        private int q;
        private int r;
        public BoardLocation Location => new BoardLocation(q, r);

        public PlacedPiece(PiecesEnum pt, ColorsEnum c, int q, int r) : base(pt, c) { this.q = q; this.r = r; }
        public PlacedPiece(PlacedPiece p, BoardLocation bl) : base(p.PieceType, p.Color) { this.q = bl.Q; this.r = bl.R; }

        public bool DeepEquals(PlacedPiece p)
        {
            return this.Color == p.Color && this.PieceType == p.PieceType && this.q == p.Location.Q && this.r == p.Location.R;
        }
    }

    public class PieceEvent
    {
        public PlacedPiece Regarding { get; private set; }
        public EventTypeEnum EventType { get; private set; }
        public PieceEvent(PlacedPiece p, EventTypeEnum t) { this.Regarding = p; this.EventType = t; }
    }

    public class PieceList : List<Piece>
    {
        public bool ContainsThePiece(PiecesEnum pt, ColorsEnum c)
        {
            return this.Any(item => item.PieceType == pt && item.Color == c);
        }

        public void RemoveThePiece(PiecesEnum pt, ColorsEnum c)
        {
            var item = this.FirstOrDefault(x => x.PieceType == pt && x.Color == c);
            if (item != null) this.Remove(item);
        }
    }

    // --- MOVEMENT RULES (STATIC) ---
    class PieceStatic
    {
        // FIX: Changed from protected to public so Board can access it
        public static BoardLocationList CookUpLocations(BoardLocation fromHere, int[,] jumpOpts)
        {
            BoardLocationList spots = new BoardLocationList();
            for (int iSet = 0; iSet < jumpOpts.GetLength(0); iSet++)
            {
                BoardLocation b = new BoardLocation(fromHere.Q + jumpOpts[iSet, 0], fromHere.R + jumpOpts[iSet, 1]);
                spots.Add(b);
            }
            return spots;
        }
    }

    class PawnStatic : PieceStatic
    {
        public static int[,] MoveOffsets = new int[,] { 
            { 0, -1 }, { 1, -1 }, { 1, 0 }, { 0, 1 }, { -1, 1 }, { -1, 0 } 
        };

        public static int[,] AttackOffsets = new int[,] {
            { 1, 1 }, { 2, -1 }, { 1, -2 }, { -1, -1 }, { -2, 1 }, { -1, 2 }
        };

        public static BoardLocationList CouldGoIfOmnipotent(BoardLocation loc)
        {
            return CookUpLocations(loc, MoveOffsets);
        }
    }

    class KingStatic : PieceStatic
    {
        public static BoardLocationList CouldGoIfOmnipotent(BoardLocation loc)
        {
            return CookUpLocations(loc, new int[,] { { 0, -1 }, { 1, -1 }, { 1, 0 }, { 0, 1 }, { -1, 1 }, { -1, 0 } });
        }
    }

    class KnightStatic : PieceStatic
    {
        public static BoardLocationList CouldGoIfOmnipotent(BoardLocation loc)
        {
            return CookUpLocations(loc, new int[,] { 
                { 1, -3 }, { 2, -3 }, { -2, -1 }, { -1, -2 }, { 3, -2 }, { 3, -1 },
                { 2, 1 }, { 1, 2 }, { -2, 3 }, {-1, 3 }, {-3, 1 }, {-3, 2 } 
            });
        }
    }

    class CastleStatic : PieceStatic
    {
        public static List<BoardLocationList> ListOfSequencesOfSpots(BoardLocation loc)
        {
            List<BoardLocationList> ll = new List<BoardLocationList>();
            ll.Add(CookUpLocations(loc, new int[,] { { 1, -1 }, { 2, -2 }, { 3, -3 }, { 4, -4 }, { 5, -5 }, { 6, -6 } })); // +q, -r
            ll.Add(CookUpLocations(loc, new int[,] { { 1, 0 }, { 2, 0 }, { 3, 0 }, { 4, 0 }, { 5, 0 }, { 6, 0 } }));        // +q
            ll.Add(CookUpLocations(loc, new int[,] { { 0, 1 }, { 0, 2 }, { 0, 3 }, { 0, 4 }, { 0, 5 }, { 0, 6 } }));        // +r
            ll.Add(CookUpLocations(loc, new int[,] { { -1, 1 }, { -2, 2 }, { -3, 3 }, { -4, 4 }, { -5, 5 }, { -6, 6 } })); // -q, +r
            ll.Add(CookUpLocations(loc, new int[,] { { -1, 0 }, { -2, 0 }, { -3, 0 }, { -4, 0 }, { -5, 0 }, { -6, 0 } })); // -q
            ll.Add(CookUpLocations(loc, new int[,] { { 0, -1 }, { 0, -2 }, { 0, -3 }, { 0, -4 }, { 0, -5 }, { 0, -6 } })); // -r
            return ll;
        }
    }

    class BishopStatic : PieceStatic
    {
        public static List<BoardLocationList> ListOfSequencesOfSpots(BoardLocation loc)
        {
            List<BoardLocationList> ll = new List<BoardLocationList>();
            ll.Add(CookUpLocations(loc, new int[,] { { 1, 1 }, { 2, 2 }, { 3, 3 }, { 4, 4 }, { 5, 5 } }));
            ll.Add(CookUpLocations(loc, new int[,] { { -1, -1 }, { -2, -2 }, { -3, -3 }, { -4, -4 }, { -5, -5 } }));
            ll.Add(CookUpLocations(loc, new int[,] { { 1, -2 }, { 2, -4 }, { 3, -6 }, { 4, -8 } }));
            ll.Add(CookUpLocations(loc, new int[,] { { -1, 2 }, { -2, 4 }, { -3, 6 }, { -4, 8 } }));
            ll.Add(CookUpLocations(loc, new int[,] { { -2, 1 }, { -4, 2 }, { -6, 3 }, { -8, 4 } }));
            ll.Add(CookUpLocations(loc, new int[,] { { 2, -1 }, { 4, -2 }, { 6, -3 }, { 8, -4 } }));
            return ll;
        }
    }

// --- BOARD ENGINE ---
public class Board
    {
        public List<PlacedPiece> PlacedPieces { get; private set; } = new List<PlacedPiece>();
        
        public PieceList SidelinedPieces
        {
            get
            {
                PieceList fullSet = new PieceList();
                foreach (ColorsEnum c in Enum.GetValues(typeof(ColorsEnum)))
                {
                    fullSet.Add(new Piece(PiecesEnum.King, c));
                    fullSet.Add(new Piece(PiecesEnum.Queen, c));
                    fullSet.Add(new Piece(PiecesEnum.Castle, c));
                    fullSet.Add(new Piece(PiecesEnum.Castle, c));
                    for (int i = 0; i < 3; i++) fullSet.Add(new Piece(PiecesEnum.Elephant, c));
                    for (int i = 0; i < 3; i++) fullSet.Add(new Piece(PiecesEnum.Pawn, c));
                }

                foreach (var piece in PlacedPieces)
                {
                    if (fullSet.ContainsThePiece(piece.PieceType, piece.Color))
                        fullSet.RemoveThePiece(piece.PieceType, piece.Color);
                }
                return fullSet;
            }
        }

        public Board() { }
        public Board(Board cloneMe)
        {
            foreach (PlacedPiece p in cloneMe.PlacedPieces)
                PlacedPieces.Add(new PlacedPiece(p.PieceType, p.Color, p.Location.Q, p.Location.R));
        }

        public void Add(PlacedPiece p)
        {
            if (!p.Location.IsValidLocation()) return; 
            
            var existing = AnyoneThere(p.Location);
            if (existing != null) Remove(existing);

            PlacedPieces.Add(p);
        }
        
        public void Remove(PlacedPiece p)
        {
            foreach (var placed in PlacedPieces)
            {
                if (placed.DeepEquals(p)) { PlacedPieces.Remove(placed); return; }
            }
        }

        // FIX: Added nullable return type (?) to fix CS8603
        public PlacedPiece? AnyoneThere(BoardLocation b)
        {
            return PlacedPieces.FirstOrDefault(pp => pp.Location.Q == b.Q && pp.Location.R == b.R);
        }

        // FIX: Added nullable return type (?)
        public PlacedPiece? FindPiece(PiecesEnum type, ColorsEnum c)
        {
            return PlacedPieces.FirstOrDefault(p => p.PieceType == type && p.Color == c);
        }

        public bool IsSquareAttacked(BoardLocation loc, ColorsEnum ignoreAttacksFrom)
        {
            foreach (var p in PlacedPieces)
            {
                if (p.Color == ignoreAttacksFrom) continue;
                
                var reach = WhereCanIReach(p);
                if (reach.ContainsTheLocation(loc)) return true;
            }
            return false;
        }

        private bool HasTwoSameColorPawnNeighbors(PlacedPiece pawn)
        {
            BoardLocationList spots = PawnStatic.CouldGoIfOmnipotent(pawn.Location);
            int count = 0;
            foreach (var spot in spots)
            {
                PlacedPiece? pp = AnyoneThere(spot);
                if (pp != null && pp.PieceType == PiecesEnum.Pawn && pp.Color == pawn.Color)
                    count++;
            }
            return count >= 2;
        }

        public List<List<PieceEvent>> WhatCanICauseWithDoo(PlacedPiece p)
        {
            return WhatCanICause(p);
        }

        protected List<List<PieceEvent>> WhatCanICause(PlacedPiece p)
        {
            List<List<PieceEvent>> outcomes = new List<List<PieceEvent>>();
            
            BoardLocationList spots = WhereCanIReach(p);

            foreach (BoardLocation spot in spots)
            {
                var occupant = AnyoneThere(spot);
                if (occupant != null && occupant.PieceType == PiecesEnum.King) 
                    continue;

                List<PieceEvent> events = EventsFromAMove(p, spot);

                if (IsMoveSuicidal(p.Color, events)) continue;

                outcomes.Add(events);
            }
            return outcomes;
        }

        private bool IsMoveSuicidal(ColorsEnum color, List<PieceEvent> events)
        {
            Board sim = new Board(this);
            foreach(var evt in events)
            {
                 if (evt.EventType == EventTypeEnum.Remove) sim.Remove(evt.Regarding);
                 if (evt.EventType == EventTypeEnum.Add) sim.Add(evt.Regarding);
            }

            var myKing = sim.FindPiece(PiecesEnum.King, color);
            if (myKing != null)
            {
                if (sim.IsSquareAttacked(myKing.Location, color)) return true;
            }
            return false;
        }

        private BoardLocationList WhereCanIReach(PlacedPiece p)
        {
            bool canEnterPortal = false;
            var portalPiece = AnyoneThere(new BoardLocation(0, 0));
            if (portalPiece == null) {
                if (p.PieceType == PiecesEnum.King) canEnterPortal = true;
            } else {
                if (p.Color != portalPiece.Color) canEnterPortal = true;
            }

            BoardLocationList options = new BoardLocationList();

            switch (p.PieceType)
            {
                case PiecesEnum.Elephant:
                    options = KnightStatic.CouldGoIfOmnipotent(p.Location);
                    break;
                case PiecesEnum.King:
                    options = KingStatic.CouldGoIfOmnipotent(p.Location);
                    break;
                
                case PiecesEnum.Pawn:
                    // 1. Move (Orthogonal, Destination must be Empty)
                    var moveSpots = PawnStatic.CookUpLocations(p.Location, PawnStatic.MoveOffsets);
                    foreach (var spot in moveSpots)
                    {
                        if (spot.IsValidLocation() && AnyoneThere(spot) == null) 
                        {
                            if (spot.IsPortal && !canEnterPortal) continue;
                            options.Add(spot);
                        }
                    }

                    // 2. Attack (Diagonal, Destination must be Enemy, Gate must be open)
                    int[,] atts = PawnStatic.AttackOffsets;
                    for(int i=0; i<atts.GetLength(0); i++)
                    {
                        int dq = atts[i,0];
                        int dr = atts[i,1];
                        BoardLocation target = new BoardLocation(p.Location.Q + dq, p.Location.R + dr);
                        if (!target.IsValidLocation()) continue;
                        if (target.IsPortal && !canEnterPortal) continue;

                        var victim = AnyoneThere(target);
                        if (victim != null && victim.Color != p.Color)
                        {
                            // Gate Check
                            BoardLocation? g1 = null; 
                            BoardLocation? g2 = null;
                            if (dq == 1 && dr == 1)       { g1 = new BoardLocation(p.Location.Q + 1, p.Location.R); g2 = new BoardLocation(p.Location.Q, p.Location.R + 1); }
                            else if (dq == 2 && dr == -1) { g1 = new BoardLocation(p.Location.Q + 1, p.Location.R); g2 = new BoardLocation(p.Location.Q + 1, p.Location.R - 1); }
                            else if (dq == 1 && dr == -2) { g1 = new BoardLocation(p.Location.Q, p.Location.R - 1); g2 = new BoardLocation(p.Location.Q + 1, p.Location.R - 1); }
                            else if (dq == -1 && dr == -1){ g1 = new BoardLocation(p.Location.Q - 1, p.Location.R); g2 = new BoardLocation(p.Location.Q, p.Location.R - 1); }
                            else if (dq == -2 && dr == 1) { g1 = new BoardLocation(p.Location.Q - 1, p.Location.R); g2 = new BoardLocation(p.Location.Q - 1, p.Location.R + 1); }
                            else if (dq == -1 && dr == 2) { g1 = new BoardLocation(p.Location.Q, p.Location.R + 1); g2 = new BoardLocation(p.Location.Q - 1, p.Location.R + 1); }

                            if ((g1 != null && AnyoneThere(g1) == null) || (g2 != null && AnyoneThere(g2) == null))
                            {
                                options.Add(target);
                            }
                        }
                    }
                    break;

                case PiecesEnum.Castle:
                    AddSlideMoves(options, CastleStatic.ListOfSequencesOfSpots(p.Location), p);
                    break;
                
                case PiecesEnum.Queen:
                    // 1. Sliding Moves (Rook + Bishop)
                    AddSlideMoves(options, CastleStatic.ListOfSequencesOfSpots(p.Location), p);
                    AddSlideMoves(options, BishopStatic.ListOfSequencesOfSpots(p.Location), p);
                    
                    // 2. Special 3-Step Diagonal Jump (Gate Logic)
                    AddQueenSpecialMoves(options, p);
                    break;
            }

            BoardLocationList final = new BoardLocationList();
            foreach (var spot in options)
            {
                if (!spot.IsValidLocation()) continue;
                if (spot.IsPortal && !canEnterPortal) continue;
                
                var occupant = AnyoneThere(spot);
                if (occupant != null) {
                    if (occupant.Color == p.Color) continue; 
                    if (occupant.PieceType == PiecesEnum.Pawn && HasTwoSameColorPawnNeighbors(occupant)) continue; // Phalanx
                }
                final.Add(spot);
            }
            return final;
        }

        private void AddQueenSpecialMoves(BoardLocationList options, PlacedPiece p)
        {
            int[,] diagData = new int[,] {
                { 1, 1,   1,0,  0,1 },   // SE
                { -1, -1, -1,0, 0,-1 },  // NW
                { 1, -2,  1,-1, 0,-1 },  // NE
                { -1, 2,  -1,1, 0,1 },   // SW
                { 2, -1,  1,0,  1,-1 },  // E
                { -2, 1,  -1,0, -1,1 }   // W
            };

            for (int i = 0; i < 6; i++)
            {
                int dq = diagData[i, 0];
                int dr = diagData[i, 1];
                int g1q = diagData[i, 2];
                int g1r = diagData[i, 3];
                int g2q = diagData[i, 4];
                int g2r = diagData[i, 5];

                BoardLocation current = p.Location;
                bool failed = false;

                for (int step = 1; step <= 3; step++)
                {
                    BoardLocation gate1 = new BoardLocation(current.Q + g1q, current.R + g1r);
                    BoardLocation gate2 = new BoardLocation(current.Q + g2q, current.R + g2r);
                    
                    bool g1Open = (AnyoneThere(gate1) == null);
                    bool g2Open = (AnyoneThere(gate2) == null);
                    if (!g1Open && !g2Open) { failed = true; break; }

                    current = new BoardLocation(current.Q + dq, current.R + dr);

                    if (step < 3)
                    {
                        if (AnyoneThere(current) != null) { failed = true; break; }
                    }
                }

                if (!failed) options.Add(current);
            }
        }

        private void AddSlideMoves(BoardLocationList options, List<BoardLocationList> runs, PlacedPiece p)
        {
            foreach(var run in runs) {
                foreach(var spot in run) {
                    if (spot.IsPortal) {
                        var pp = AnyoneThere(spot);
                        if (pp == null) break; 
                        if (pp.Color == p.Color) break; 
                    }
                    var occupant = AnyoneThere(spot);
                    if (occupant == null) {
                        options.Add(spot);
                    } else {
                        if (occupant.Color != p.Color) options.Add(spot);
                        break; 
                    }
                }
            }
        }

        private List<PieceEvent> EventsFromAMove(PlacedPiece p, BoardLocation spot)
        {
            List<PieceEvent> events = new List<PieceEvent>();
            events.Add(new PieceEvent(p, EventTypeEnum.Remove));

            PlacedPiece? deadp = AnyoneThere(spot);
            if (deadp != null)
            {
                events.Add(new PieceEvent(deadp, EventTypeEnum.Remove));

                var portalOccupant = AnyoneThere(new BoardLocation(0, 0));
                bool isPortalAvailable = (portalOccupant == null) || 
                                         (deadp.Location.IsPortal) || 
                                         (portalOccupant == p);

                if (SidelinedPieces.ContainsThePiece(deadp.PieceType, p.Color) && isPortalAvailable)
                {
                   events.Add(new PieceEvent(new PlacedPiece(deadp.PieceType, p.Color, 0, 0), EventTypeEnum.Add));
                }
            }

            bool pieceSurvives = true;
            if (spot.IsPortal)
            {
                if (p.PieceType != PiecesEnum.King) pieceSurvives = false;
            }

            if (pieceSurvives)
            {
                events.Add(new PieceEvent(new PlacedPiece(p, spot), EventTypeEnum.Add));
            }
            return events;
        }
    }
    
// --- GAME CONTROLLER ---
    public class Game
    {
        public Board Board { get; private set; }
        public ColorsEnum CurrentTurn { get; private set; }
        public GameStateEnum State { get; private set; }
        public string? StatusMessage { get; private set; }
        public bool MainMovePending { get; private set; } 
        
        private List<ColorsEnum> TurnOrder = new List<ColorsEnum> { ColorsEnum.Blue, ColorsEnum.White, ColorsEnum.Red };

        public Game()
        {
            Board = new Board();
            SetupStandardBoard(Board);

            CurrentTurn = ColorsEnum.Blue; 
            State = GameStateEnum.Active;
            StatusMessage = "Game Started. Blue to move.";
            MainMovePending = false;
        }

        public void LoadMatchState(Board b, ColorsEnum turn)
        {
            Board = b;
            CurrentTurn = turn;
            StatusMessage = $"Game Loaded. {CurrentTurn} to move.";
        }

        private void SetupStandardBoard(Board b)
        {
            // BLUE
            b.Add(new PlacedPiece(PiecesEnum.Castle, ColorsEnum.Blue, -1, -4));
            b.Add(new PlacedPiece(PiecesEnum.Castle, ColorsEnum.Blue, -4, -1));
            b.Add(new PlacedPiece(PiecesEnum.Elephant, ColorsEnum.Blue, -1, -3));
            b.Add(new PlacedPiece(PiecesEnum.Elephant, ColorsEnum.Blue, -2, -2));
            b.Add(new PlacedPiece(PiecesEnum.Elephant, ColorsEnum.Blue, -3, -1));
            b.Add(new PlacedPiece(PiecesEnum.Pawn, ColorsEnum.Blue, -1, -2));
            b.Add(new PlacedPiece(PiecesEnum.Pawn, ColorsEnum.Blue, -1, -1));
            b.Add(new PlacedPiece(PiecesEnum.Pawn, ColorsEnum.Blue, -2, -1));
            b.Add(new PlacedPiece(PiecesEnum.Queen, ColorsEnum.Blue, -3, -2));
            b.Add(new PlacedPiece(PiecesEnum.King, ColorsEnum.Blue, -2, -3));

            // RED
            b.Add(new PlacedPiece(PiecesEnum.Castle, ColorsEnum.Red, -4, 5));
            b.Add(new PlacedPiece(PiecesEnum.Castle, ColorsEnum.Red, -1, 5));
            b.Add(new PlacedPiece(PiecesEnum.Elephant, ColorsEnum.Red, -3, 4));
            b.Add(new PlacedPiece(PiecesEnum.Elephant, ColorsEnum.Red, -2, 4));
            b.Add(new PlacedPiece(PiecesEnum.Elephant, ColorsEnum.Red, -1, 4));
            b.Add(new PlacedPiece(PiecesEnum.Pawn, ColorsEnum.Red, -2, 3));
            b.Add(new PlacedPiece(PiecesEnum.Pawn, ColorsEnum.Red, -1, 3));
            b.Add(new PlacedPiece(PiecesEnum.Pawn, ColorsEnum.Red, -1, 2));
            b.Add(new PlacedPiece(PiecesEnum.King, ColorsEnum.Red, -3, 5));
            b.Add(new PlacedPiece(PiecesEnum.Queen, ColorsEnum.Red, -2, 5));

            // WHITE
            b.Add(new PlacedPiece(PiecesEnum.Castle, ColorsEnum.White, 5, -4));
            b.Add(new PlacedPiece(PiecesEnum.Castle, ColorsEnum.White, 5, -1));
            b.Add(new PlacedPiece(PiecesEnum.Elephant, ColorsEnum.White, 4, -3));
            b.Add(new PlacedPiece(PiecesEnum.Elephant, ColorsEnum.White, 4, -2));
            b.Add(new PlacedPiece(PiecesEnum.Elephant, ColorsEnum.White, 4, -1));
            b.Add(new PlacedPiece(PiecesEnum.Pawn, ColorsEnum.White, 3, -2));
            b.Add(new PlacedPiece(PiecesEnum.Pawn, ColorsEnum.White, 3, -1));
            b.Add(new PlacedPiece(PiecesEnum.Pawn, ColorsEnum.White, 2, -1));
            b.Add(new PlacedPiece(PiecesEnum.King, ColorsEnum.White, 5, -3));
            b.Add(new PlacedPiece(PiecesEnum.Queen, ColorsEnum.White, 5, -2));
        }

        public void SubmitMove(int q1, int r1, int q2, int r2)
        {
            if (State == GameStateEnum.Finished) return;

            var piece = Board.AnyoneThere(new BoardLocation(q1, r1));
            if (piece == null) { StatusMessage = "No piece selected."; return; }
            if (piece.Color != CurrentTurn) { StatusMessage = $"It is {CurrentTurn}'s turn!"; return; }

            // --- 1. SWAP LOGIC (Diddilydoo) ---
            var targetPiece = Board.AnyoneThere(new BoardLocation(q2, r2));
            if (targetPiece != null && targetPiece.Color == piece.Color)
            {
                bool isKingQueen = (piece.PieceType == PiecesEnum.King && targetPiece.PieceType == PiecesEnum.Queen);
                bool isQueenKing = (piece.PieceType == PiecesEnum.Queen && targetPiece.PieceType == PiecesEnum.King);

                if (isKingQueen || isQueenKing)
                {
                    if (MainMovePending)
                    {
                        StatusMessage = "You cannot Swap again. Please make your Main Move.";
                        return;
                    }

                    Board sim = new Board(Board);
                    sim.Remove(piece);
                    sim.Remove(targetPiece);
                    sim.Add(new PlacedPiece(piece.PieceType, piece.Color, q2, r2));
                    sim.Add(new PlacedPiece(targetPiece.PieceType, targetPiece.Color, q1, r1));
                    
                    var myKing = sim.FindPiece(PiecesEnum.King, CurrentTurn);
                    if (myKing != null && sim.IsSquareAttacked(myKing.Location, CurrentTurn))
                    {
                        StatusMessage = "You cannot swap into Check!";
                        return;
                    }

                    Board.Remove(piece);
                    Board.Remove(targetPiece);
                    Board.Add(new PlacedPiece(piece.PieceType, piece.Color, q2, r2));
                    Board.Add(new PlacedPiece(targetPiece.PieceType, targetPiece.Color, q1, r1));
                    
                    MainMovePending = true;
                    StatusMessage = $"{piece.PieceType}-{targetPiece.PieceType} Swap (Diddilydoo). Make your Main Move.";
                    return; 
                }
                else
                {
                    StatusMessage = "You cannot move onto your own piece.";
                    return;
                }
            }

            // --- 2. STANDARD MOVE LOGIC ---
            var options = Board.WhatCanICauseWithDoo(piece);
            bool isValid = false;
            List<PieceEvent> validEvents = null;

            foreach(var eventSet in options)
            {
                foreach(var evt in eventSet)
                {
                    if (evt.EventType == EventTypeEnum.Add && 
                        evt.Regarding.Location.Q == q2 && 
                        evt.Regarding.Location.R == r2 &&
                        evt.Regarding.PieceType == piece.PieceType)
                    {
                        isValid = true;
                        validEvents = eventSet;
                        break;
                    }
                }
                if (isValid) break;
            }

            if (isValid)
            {
                bool spawnedInPortal = false;
                PlacedPiece? captured = null;
                
                foreach(var evt in validEvents)
                {
                    if (evt.EventType == EventTypeEnum.Remove) 
                    {
                        if (evt.Regarding != piece) captured = evt.Regarding; 
                        Board.Remove(evt.Regarding);
                    }
                    if (evt.EventType == EventTypeEnum.Add) 
                    {
                        Board.Add(evt.Regarding);
                        if (evt.Regarding.Location.Q == 0 && evt.Regarding.Location.R == 0)
                        {
                            if (evt.Regarding != piece) spawnedInPortal = true;
                        }
                    }
                }

                var portalOccupant = Board.AnyoneThere(new BoardLocation(0,0));
                if (portalOccupant != null && portalOccupant.PieceType == PiecesEnum.King)
                {
                    State = GameStateEnum.Finished;
                    StatusMessage = $"{portalOccupant.Color} Wins by Portal!";
                    return;
                }
                
                string entropyMsg = "";

                if (q2 == 0 && r2 == 0 && piece.PieceType != PiecesEnum.King)
                {
                    var suicidePiece = Board.FindPiece(piece.PieceType, piece.Color); 
                    var currentAtZero = Board.AnyoneThere(new BoardLocation(0,0));

                    if (currentAtZero != null && 
                        currentAtZero.Color == piece.Color && 
                        currentAtZero.PieceType == piece.PieceType)
                    {
                         Board.Remove(currentAtZero);
                         entropyMsg = " (Attacker vanished in the Portal)";
                    }
                }
                else 
                {
                    var camper = Board.AnyoneThere(new BoardLocation(0,0));
                    if (camper != null && 
                        camper.Color == CurrentTurn && 
                        camper.PieceType != PiecesEnum.King && 
                        !spawnedInPortal)
                    {
                        Board.Remove(camper);
                        entropyMsg = " (Abandoned piece lost to the Portal)";
                    }
                }

                string actionDesc = $"{CurrentTurn} {piece.PieceType} moves";
                if (captured != null)
                {
                    actionDesc = $"{CurrentTurn} {piece.PieceType} captures {captured.Color} {captured.PieceType}";
                }
                if (spawnedInPortal)
                {
                    var spawned = Board.AnyoneThere(new BoardLocation(0,0));
                    actionDesc += $" (Reincarnated {spawned?.PieceType})";
                }

                actionDesc += entropyMsg;

                if (MainMovePending)
                {
                    MainMovePending = false;
                    AdvanceTurn(actionDesc);
                }
                else
                {
                    AdvanceTurn(actionDesc);
                }
            }
            else
            {
                StatusMessage = "Invalid Move.";
            }
        }

        private void AdvanceTurn(string lastAction = "")
        {
            int currentIdx = TurnOrder.IndexOf(CurrentTurn);
            currentIdx = (currentIdx + 1) % 3;
            CurrentTurn = TurnOrder[currentIdx];
            
            CheckVictoryAtStartOfTurn();
            
            if (State != GameStateEnum.Finished)
            {
                List<string> alerts = new List<string>();

                if (StatusMessage != null && StatusMessage.Contains("Check!")) 
                {
                    alerts.Add(StatusMessage); 
                }

                int nextIdx = (TurnOrder.IndexOf(CurrentTurn) + 1) % 3;
                ColorsEnum thirdPlayer = TurnOrder[nextIdx];
                
                string thirdPlayerStatus = GetPlayerStatus(thirdPlayer);
                if (!string.IsNullOrEmpty(thirdPlayerStatus))
                {
                    alerts.Add(thirdPlayerStatus);
                }

                string alertText = string.Join(" ", alerts);
                if (!string.IsNullOrEmpty(alertText)) alertText = " " + alertText;

                StatusMessage = $"{lastAction}.{alertText}";
            }
        }

        private void CheckVictoryAtStartOfTurn()
        {
            var attackers = GetAttackers(CurrentTurn);
            if (!attackers.Any()) return; 

            if (CanEscape(CurrentTurn))
            {
                StatusMessage = $"{CurrentTurn} is in Check!";
                return;
            }

            State = GameStateEnum.Finished;

            if (attackers.Count == 1)
            {
                StatusMessage = $"{attackers[0]} Wins by Checkmate!";
            }
            else
            {
                int myIdx = TurnOrder.IndexOf(CurrentTurn);
                int prevIdx = (myIdx + 2) % 3; // Right
                int prevPrevIdx = (myIdx + 1) % 3; // Left

                ColorsEnum winner = TurnOrder[prevPrevIdx]; 
                
                if (!attackers.Contains(winner))
                {
                    winner = TurnOrder[prevIdx];
                }

                StatusMessage = $"{winner} Wins by Priority Checkmate!";
            }
        }

        private string GetPlayerStatus(ColorsEnum color)
        {
            var attackers = GetAttackers(color);
            if (!attackers.Any()) return ""; 

            if (CanEscape(color))
            {
                return $"{color} is in Check!";
            }
            else
            {
                return $"{color} is in GRAVE DANGER (Pending Checkmate)!";
            }
        }

        private List<ColorsEnum> GetAttackers(ColorsEnum victimColor)
        {
            var king = Board.FindPiece(PiecesEnum.King, victimColor);
            List<ColorsEnum> attackers = new List<ColorsEnum>();
            if (king == null) return attackers; 

            if (Board.IsSquareAttacked(king.Location, victimColor))
            {
                foreach(ColorsEnum enemyColor in Enum.GetValues(typeof(ColorsEnum)))
                {
                    if (enemyColor == victimColor) continue;
                    
                    if (Board.PlacedPieces.Any(pp => pp.Color == enemyColor && 
                        Board.WhatCanICauseWithDoo(pp).Any(es => es.Any(e => 
                            e.EventType == EventTypeEnum.Add && 
                            e.Regarding.Location.Q == king.Location.Q && 
                            e.Regarding.Location.R == king.Location.R))))
                    {
                        attackers.Add(enemyColor);
                    }
                }
            }
            return attackers.Distinct().ToList();
        }

        private bool CanEscape(ColorsEnum victimColor)
        {
            var myPieces = Board.PlacedPieces.Where(p => p.Color == victimColor).ToList();

            foreach(var p in myPieces)
            {
                var outcomes = Board.WhatCanICauseWithDoo(p);
                foreach(var eventSet in outcomes)
                {
                    Board simBoard = new Board(Board);
                    
                    foreach(var evt in eventSet)
                    {
                        if (evt.EventType == EventTypeEnum.Remove) simBoard.Remove(evt.Regarding);
                        if (evt.EventType == EventTypeEnum.Add) simBoard.Add(evt.Regarding);
                    }

                    var simKing = simBoard.FindPiece(PiecesEnum.King, victimColor);
                    if (simKing != null)
                    {
                        if (!simBoard.IsSquareAttacked(simKing.Location, victimColor)) 
                            return true; 
                    }
                }
            }
            return false; 
        }
    }
}