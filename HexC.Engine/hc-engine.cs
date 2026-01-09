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

        // --- ADD THIS METHOD ---
        public static bool IsSameLocation(BoardLocation one, BoardLocation two)
        {
            if (one is null || two is null) return false;
            return one.Q == two.Q && one.R == two.R;
        }
        // -----------------------

        public bool IsPortal => (q == 0 && r == 0);
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
        protected static BoardLocationList CookUpLocations(BoardLocation fromHere, int[,] jumpOpts)
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
        public static BoardLocationList CouldGoIfOmnipotent(BoardLocation loc)
        {
            return CookUpLocations(loc, new int[,] { { 0, -1 }, { 1, -1 }, { 1, 0 }, { 0, 1 }, { -1, 1 }, { -1, 0 } });
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
            ll.Add(CookUpLocations(loc, new int[,] { { 1, 0 }, { 2, 0 }, { 3, 0 }, { 4, 0 }, { 5, 0 }, { 6, 0 } }));       // +q
            ll.Add(CookUpLocations(loc, new int[,] { { 0, 1 }, { 0, 2 }, { 0, 3 }, { 0, 4 }, { 0, 5 }, { 0, 6 } }));       // +r
            ll.Add(CookUpLocations(loc, new int[,] { { -1, 1 }, { -2, 2 }, { -3, 3 }, { -4, 4 }, { -5, 5 }, { -6, 6 } })); // -q, +r
            ll.Add(CookUpLocations(loc, new int[,] { { -1, 0 }, { -2, 0 }, { -3, 0 }, { -4, 0 }, { -5, 0 }, { -6, 0 } })); // -q
            ll.Add(CookUpLocations(loc, new int[,] { { 0, -1 }, { 0, -2 }, { 0, -3 }, { 0, -4 }, { 0, -5 }, { 0, -6 } })); // -r
            return ll;
        }
    }

// --- BOARD ENGINE ---
public class Board
    {
        public List<PlacedPiece> PlacedPieces { get; private set; } = new List<PlacedPiece>();
        public static BoardLocationList m_QueenDesties = new BoardLocationList(); 

        // --- THIS WAS MISSING ---
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
        // ------------------------

        public Board() { }
        public Board(Board cloneMe)
        {
            foreach (PlacedPiece p in cloneMe.PlacedPieces)
                PlacedPieces.Add(p);
        }

        public void Add(PlacedPiece p)
        {
            // 1. Strict Validation
            if (!p.Location.IsValidLocation())
            {
                throw new ArgumentException($"Cannot place piece at {p.Location.Q},{p.Location.R} - Location is invalid.");
            }

            // 2. Strict Overlap Check
            if (AnyoneThere(p.Location) != null)
            {
                throw new InvalidOperationException($"Cannot place piece at {p.Location.Q},{p.Location.R} - Spot is already occupied.");
            }

            PlacedPieces.Add(p);
        }
        
        public void Remove(PlacedPiece p)
        {
            foreach (var placed in PlacedPieces)
            {
                if (placed.DeepEquals(p)) { PlacedPieces.Remove(placed); return; }
            }
        }

        public void BruteForceMove(int q1, int r1, int q2, int r2)
        {
            BoardLocation loc = new BoardLocation(q1, r1);
            var piece = AnyoneThere(loc);
            if (piece != null)
            {
                this.Remove(piece);
                var destPiece = AnyoneThere(new BoardLocation(q2, r2));
                if (destPiece != null) this.Remove(destPiece);
                this.Add(new PlacedPiece(piece.PieceType, piece.Color, q2, r2));
            }
        }

        public PlacedPiece AnyoneThere(BoardLocation b)
        {
            return PlacedPieces.FirstOrDefault(pp => pp.Location.Q == b.Q && pp.Location.R == b.R);
        }

        public PlacedPiece FindPiece(PiecesEnum type, ColorsEnum c)
        {
            return PlacedPieces.FirstOrDefault(p => p.PieceType == type && p.Color == c);
        }

        private bool HasTwoSameColorPawnNeighbors(PlacedPiece pawn)
        {
            BoardLocationList spots = PawnStatic.CouldGoIfOmnipotent(pawn.Location);
            int count = 0;
            foreach (var spot in spots)
            {
                PlacedPiece pp = AnyoneThere(spot);
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
                List<PieceEvent> events = EventsFromAMove(p, spot);
                outcomes.Add(events);
            }
            return outcomes;
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
                    options = PawnStatic.CouldGoIfOmnipotent(p.Location);
                    break;
                case PiecesEnum.Castle:
                case PiecesEnum.Queen:
                    List<BoardLocationList> runs = CastleStatic.ListOfSequencesOfSpots(p.Location);
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

        private List<PieceEvent> EventsFromAMove(PlacedPiece p, BoardLocation spot)
        {
            List<PieceEvent> events = new List<PieceEvent>();
            events.Add(new PieceEvent(p, EventTypeEnum.Remove));

            PlacedPiece deadp = AnyoneThere(spot);
            if (deadp != null)
            {
                events.Add(new PieceEvent(deadp, EventTypeEnum.Remove));
                if (SidelinedPieces.ContainsThePiece(deadp.PieceType, p.Color) && 
                   (AnyoneThere(new BoardLocation(0,0)) == null || deadp.Location.IsPortal))
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
        public string StatusMessage { get; private set; }
        public bool MainMovePending { get; private set; } 
        
        private List<ColorsEnum> TurnOrder = new List<ColorsEnum> { ColorsEnum.Blue, ColorsEnum.White, ColorsEnum.Red };
        private Dictionary<ColorsEnum, bool> PlayerEliminated = new Dictionary<ColorsEnum, bool>();

        public Game()
        {
            Board = new Board();
            SetupStandardBoard(Board);

            CurrentTurn = ColorsEnum.Blue; 
            State = GameStateEnum.Active;
            StatusMessage = "Game Started. Blue to move.";
            MainMovePending = false;
            
            PlayerEliminated[ColorsEnum.White] = false;
            PlayerEliminated[ColorsEnum.Red] = false;
            PlayerEliminated[ColorsEnum.Blue] = false;
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

                    Board.Remove(piece);
                    Board.Remove(targetPiece);
                    Board.Add(new PlacedPiece(piece.PieceType, piece.Color, q2, r2));
                    Board.Add(new PlacedPiece(targetPiece.PieceType, targetPiece.Color, q1, r1));
                    
                    MainMovePending = true;
                    StatusMessage = "Diddilydoo complete. Make your Main Move.";
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
                // Execute Events
                bool spawnedInPortal = false;
                
                foreach(var evt in validEvents)
                {
                    if (evt.EventType == EventTypeEnum.Remove) 
                    {
                        Board.Remove(evt.Regarding);
                    }
                    if (evt.EventType == EventTypeEnum.Add) 
                    {
                        // Special Handling: If we are capturing in the portal, 
                        // we might have an "Add Attacker" event AND an "Add Spawn" event for 0,0.
                        // We must allow the Add, even if 0,0 is temporarily occupied.
                        // (Board.Add usually overwrites or we assume validation passed).
                        Board.Add(evt.Regarding);

                        // If something was added to 0,0, was it a Reincarnation?
                        // If it's the piece we moved, it's NOT a Reincarnation (it's a Move).
                        if (evt.Regarding.Location.Q == 0 && evt.Regarding.Location.R == 0)
                        {
                            if (evt.Regarding != piece) // If it's not the piece we clicked...
                                spawnedInPortal = true; // ...then it must be a Reincarnation.
                        }
                    }
                }

                // --- CHECK VICTORY (King in Portal) ---
                var portalOccupant = Board.AnyoneThere(new BoardLocation(0,0));
                if (portalOccupant != null && portalOccupant.PieceType == PiecesEnum.King)
                {
                    State = GameStateEnum.Finished;
                    StatusMessage = $"{portalOccupant.Color} Wins by Portal!";
                    return;
                }

                CheckEliminations();
                
                if (State != GameStateEnum.Finished)
                {
                    // --- PORTAL ENTROPY LOGIC ---
                    string entropyMsg = "";

                    // CASE A: SUICIDE (Attacker entered Portal)
                    // If the acting piece moved to 0,0 and is NOT a King...
                    if (q2 == 0 && r2 == 0 && piece.PieceType != PiecesEnum.King)
                    {
                        // We must remove the ATTACKER.
                        // BEWARE: If Reincarnation happened, 0,0 might hold the NEW piece.
                        // We must specifically remove the piece that matches the Attacker.
                        var suicidePiece = Board.FindPiece(piece.PieceType, piece.Color); 
                        // Note: FindPiece finds *a* piece. We need the one at 0,0.
                        var currentAtZero = Board.AnyoneThere(new BoardLocation(0,0));

                        if (currentAtZero != null && 
                            currentAtZero.Color == piece.Color && 
                            currentAtZero.PieceType == piece.PieceType)
                        {
                             Board.Remove(currentAtZero);
                             entropyMsg = " (Attacker vanished in the Portal)";
                        }
                    }
                    // CASE B: NEGLECT (Piece left in Portal from previous turn)
                    else 
                    {
                        // If there is a piece at 0,0 belonging to current player...
                        var camper = Board.AnyoneThere(new BoardLocation(0,0));
                        if (camper != null && 
                            camper.Color == CurrentTurn && 
                            camper.PieceType != PiecesEnum.King && 
                            !spawnedInPortal) // If it didn't JUST appear...
                        {
                            Board.Remove(camper);
                            entropyMsg = " (Abandoned piece lost to the Portal)";
                        }
                    }

                    if (MainMovePending)
                    {
                        MainMovePending = false;
                        AdvanceTurn();
                    }
                    else
                    {
                        AdvanceTurn();
                    }

                    if (!string.IsNullOrEmpty(entropyMsg)) StatusMessage += entropyMsg;
                }
            }
            else
            {
                StatusMessage = "Invalid Move.";
            }
        }

        private void CheckEliminations()
        {
            foreach(ColorsEnum c in Enum.GetValues(typeof(ColorsEnum)))
            {
                if (PlayerEliminated[c]) continue;
                if (Board.FindPiece(PiecesEnum.King, c) == null)
                {
                    PlayerEliminated[c] = true;
                    StatusMessage = $"{c} has been eliminated!";
                }
            }

            int activePlayers = PlayerEliminated.Count(x => x.Value == false);
            if (activePlayers == 1)
            {
                var winner = PlayerEliminated.FirstOrDefault(x => x.Value == false).Key;
                State = GameStateEnum.Finished;
                StatusMessage = $"{winner} Wins by Elimination!";
            }
        }

        private void AdvanceTurn()
        {
            int currentIdx = TurnOrder.IndexOf(CurrentTurn);
            int attempts = 0;
            do 
            {
                currentIdx = (currentIdx + 1) % 3;
                CurrentTurn = TurnOrder[currentIdx];
                attempts++;
            } 
            while (PlayerEliminated[CurrentTurn] && attempts < 4);

            StatusMessage = $"{CurrentTurn}'s Turn.";
        }
    }
}