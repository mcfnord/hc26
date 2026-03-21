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

        public static bool IsAdjacent(BoardLocation a, BoardLocation b)
        {
            int dq = Math.Abs(a.Q - b.Q);
            int dr = Math.Abs(a.R - b.R);
            int ds = Math.Abs((a.Q + a.R) - (b.Q + b.R));
            return dq <= 1 && dr <= 1 && ds <= 1 && (dq + dr + ds == 2);
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

    // --- THREAT INFO (for UI feedback on illegal moves) ---
    public class ThreatInfo
    {
        public PlacedPiece Attacker { get; set; }
        public List<BoardLocation> Path { get; set; } = new List<BoardLocation>();
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

    class ElephantStatic : PieceStatic
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
            int maxDistance = 10; // Maximum edge-to-edge distance on a radius 5 board
            
            int[,] directions = new int[,] {
                { 1, -1 }, // +q, -r
                { 1, 0 },  // +q
                { 0, 1 },  // +r
                { -1, 1 }, // -q, +r
                { -1, 0 }, // -q
                { 0, -1 }  // -r
            };

            for (int d = 0; d < directions.GetLength(0); d++)
            {
                int[,] moves = new int[maxDistance, 2];
                for (int i = 0; i < maxDistance; i++)
                {
                    moves[i, 0] = directions[d, 0] * (i + 1);
                    moves[i, 1] = directions[d, 1] * (i + 1);
                }
                ll.Add(CookUpLocations(loc, moves));
            }
            
            return ll;
        }
    }

    class DiagonalStatic : PieceStatic
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

        /// <summary>
        /// Returns the distinct enemy colors whose pieces can reach 'target'.
        /// Uses WhereCanIReach (the same reachability logic as IsSquareAttacked),
        /// which correctly includes squares occupied by Kings — unlike
        /// WhatCanICauseWithDoo which deliberately filters those out.
        /// </summary>
        public List<ColorsEnum> GetAttackingColors(BoardLocation target, ColorsEnum friendlyColor)
        {
            var colors = new HashSet<ColorsEnum>();
            foreach (var p in PlacedPieces)
            {
                if (p.Color == friendlyColor) continue;
                var reach = WhereCanIReach(p);
                if (reach.ContainsTheLocation(target))
                    colors.Add(p.Color);
            }
            return colors.ToList();
        }

        /// <summary>
        /// Returns all enemy pieces that can reach 'target', along with their
        /// slide/attack paths. Used by the UI to show WHY a move into check
        /// is illegal.
        /// </summary>
        public List<ThreatInfo> GetThreatsToSquare(
            BoardLocation target, ColorsEnum friendlyColor)
        {
            var threats = new List<ThreatInfo>();

            foreach (var p in PlacedPieces)
            {
                if (p.Color == friendlyColor) continue;

                var reach = WhereCanIReach(p);
                if (reach.ContainsTheLocation(target))
                {
                    var threat = new ThreatInfo { Attacker = p };

                    // Compute visual path for sliding pieces
                    if (p.PieceType == PiecesEnum.Castle
                        || p.PieceType == PiecesEnum.Queen)
                    {
                        threat.Path = ComputeSlidePath(p.Location, target);
                    }
                    // For pawn diagonal attacks, show the two gate hexes
                    else if (p.PieceType == PiecesEnum.Pawn)
                    {
                        threat.Path = ComputePawnAttackGates(p.Location, target);
                    }

                    threats.Add(threat);
                }
            }

            return threats;
        }

        /// <summary>
        /// For a sliding piece, returns the intermediate hexes between 'from'
        /// and 'to' along a cardinal hex axis. Returns empty if not on a
        /// shared axis (e.g. queen diagonal jump).
        /// </summary>
        private List<BoardLocation> ComputeSlidePath(
            BoardLocation from, BoardLocation to)
        {
            var path = new List<BoardLocation>();

            int dq = to.Q - from.Q;
            int dr = to.R - from.R;

            int stepQ = 0, stepR = 0;

            // Determine if they share a cardinal hex axis
            if (dq == 0 && dr != 0)
                { stepR = dr > 0 ? 1 : -1; }
            else if (dr == 0 && dq != 0)
                { stepQ = dq > 0 ? 1 : -1; }
            else if (dq != 0 && dq == -dr)
                { stepQ = dq > 0 ? 1 : -1; stepR = -stepQ; }
            else
                return path; // Not on a cardinal axis

            int q = from.Q + stepQ;
            int r = from.R + stepR;

            while (!(q == to.Q && r == to.R))
            {
                path.Add(new BoardLocation(q, r));
                q += stepQ;
                r += stepR;

                if (path.Count > 12) break; // safety
            }

            return path;
        }

        /// <summary>
        /// For a pawn diagonal attack, returns the two gate hexes that must
        /// have at least one opening.
        /// </summary>
        private List<BoardLocation> ComputePawnAttackGates(
            BoardLocation from, BoardLocation to)
        {
            var gates = new List<BoardLocation>();
            int dq = to.Q - from.Q;
            int dr = to.R - from.R;

            BoardLocation? g1 = null, g2 = null;

            if      (dq ==  1 && dr ==  1) { g1 = new(from.Q + 1, from.R);     g2 = new(from.Q,     from.R + 1); }
            else if (dq ==  2 && dr == -1) { g1 = new(from.Q + 1, from.R);     g2 = new(from.Q + 1, from.R - 1); }
            else if (dq ==  1 && dr == -2) { g1 = new(from.Q,     from.R - 1); g2 = new(from.Q + 1, from.R - 1); }
            else if (dq == -1 && dr == -1) { g1 = new(from.Q - 1, from.R);     g2 = new(from.Q,     from.R - 1); }
            else if (dq == -2 && dr ==  1) { g1 = new(from.Q - 1, from.R);     g2 = new(from.Q - 1, from.R + 1); }
            else if (dq == -1 && dr ==  2) { g1 = new(from.Q,     from.R + 1); g2 = new(from.Q - 1, from.R + 1); }

            if (g1 != null) gates.Add(g1);
            if (g2 != null) gates.Add(g2);
            return gates;
        }

        private bool HasTwoSameColorPawnNeighbors(PlacedPiece pawn)
        {
            BoardLocationList spots = PawnStatic.CouldGoIfOmnipotent(pawn.Location);
            List<PlacedPiece> neighbors = new List<PlacedPiece>();

            // Collect all friendly pawn neighbors
            foreach (var spot in spots)
            {
                PlacedPiece? pp = AnyoneThere(spot);
                if (pp != null && pp.PieceType == PiecesEnum.Pawn && pp.Color == pawn.Color)
                {
                    neighbors.Add(pp);
                }
            }

            // A mob requires the pawn to have at least 2 neighbors
            if (neighbors.Count < 2) return false;

            // Verify if any two of those neighbors are adjacent to EACH OTHER (forming a triangle)
            for (int i = 0; i < neighbors.Count; i++)
            {
                for (int j = i + 1; j < neighbors.Count; j++)
                {
                    if (BoardLocation.IsAdjacent(neighbors[i].Location, neighbors[j].Location))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public List<List<PieceEvent>> WhatCanICauseWithDoo(PlacedPiece p)
        {
            return WhatCanICause(p);
        }

        protected List<List<PieceEvent>> WhatCanICause(PlacedPiece p)
        {
            List<List<PieceEvent>> outcomes = new List<List<PieceEvent>>();

            BoardLocationList spots = WhereCanIReach(p);

            bool isMobLocked = (p.PieceType == PiecesEnum.Pawn && HasTwoSameColorPawnNeighbors(p));

            foreach (BoardLocation spot in spots)
            {
                var occupant = AnyoneThere(spot);
                if (occupant != null && occupant.PieceType == PiecesEnum.King)
                    continue;

                // Mob pawns cannot capture
                if (isMobLocked && occupant != null && occupant.Color != p.Color)
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
                    options = ElephantStatic.CouldGoIfOmnipotent(p.Location);
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
                    // 1. Sliding Moves (Castle)
                    AddSlideMoves(options, CastleStatic.ListOfSequencesOfSpots(p.Location), p);
                    
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

            for (int d1 = 0; d1 < 6; d1++)
            {
                BoardLocation pos0 = p.Location;

                BoardLocation gate1a = new BoardLocation(pos0.Q + diagData[d1, 2], pos0.R + diagData[d1, 3]);
                BoardLocation gate1b = new BoardLocation(pos0.Q + diagData[d1, 4], pos0.R + diagData[d1, 5]);
                if (AnyoneThere(gate1a) != null && AnyoneThere(gate1b) != null) continue;

                BoardLocation pos1 = new BoardLocation(pos0.Q + diagData[d1, 0], pos0.R + diagData[d1, 1]);
                if (!pos1.IsValidLocation()) continue;
                if (AnyoneThere(pos1) != null) continue;

                for (int d2 = 0; d2 < 6; d2++)
                {
                    BoardLocation gate2a = new BoardLocation(pos1.Q + diagData[d2, 2], pos1.R + diagData[d2, 3]);
                    BoardLocation gate2b = new BoardLocation(pos1.Q + diagData[d2, 4], pos1.R + diagData[d2, 5]);
                    if (AnyoneThere(gate2a) != null && AnyoneThere(gate2b) != null) continue;

                    BoardLocation pos2 = new BoardLocation(pos1.Q + diagData[d2, 0], pos1.R + diagData[d2, 1]);
                    if (!pos2.IsValidLocation()) continue;
                    if (AnyoneThere(pos2) != null && !BoardLocation.IsSameLocation(pos2, p.Location)) continue;

                    for (int d3 = 0; d3 < 6; d3++)
                    {
                        BoardLocation gate3a = new BoardLocation(pos2.Q + diagData[d3, 2], pos2.R + diagData[d3, 3]);
                        BoardLocation gate3b = new BoardLocation(pos2.Q + diagData[d3, 4], pos2.R + diagData[d3, 5]);
                        if (AnyoneThere(gate3a) != null && AnyoneThere(gate3b) != null) continue;

                        BoardLocation pos3 = new BoardLocation(pos2.Q + diagData[d3, 0], pos2.R + diagData[d3, 1]);
                        if (!pos3.IsValidLocation()) continue;
                        if (BoardLocation.IsSameLocation(pos3, p.Location)) continue;

                        if (!options.ContainsTheLocation(pos3))
                            options.Add(pos3);
                    }
                }
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
    
// --- GAME SNAPSHOT ---
    public class GameSnapshot
    {
        public Board Board { get; private set; }
        public ColorsEnum CurrentTurn { get; private set; }
        public GameStateEnum State { get; private set; }
        public string? StatusMessage { get; private set; }
        public bool MainMovePending { get; private set; }
        public List<string> MoveHistory { get; private set; }

        public GameSnapshot(Game game)
        {
            Board = new Board(game.Board);
            CurrentTurn = game.CurrentTurn;
            State = game.State;
            StatusMessage = game.StatusMessage;
            MainMovePending = game.MainMovePending;
            MoveHistory = new List<string>(game.MoveHistory);
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
        public List<string> MoveHistory { get; private set; } = new List<string>(); 
        
        private Stack<GameSnapshot> _history = new Stack<GameSnapshot>();
        private List<ColorsEnum> TurnOrder = new List<ColorsEnum> { ColorsEnum.Blue, ColorsEnum.White, ColorsEnum.Red };

        public Game()
        {
            _history.Clear();
            Board = new Board();
            SetupStandardBoard(Board);

            CurrentTurn = ColorsEnum.Blue;
            State = GameStateEnum.Active;
            StatusMessage = "Game Started. Blue to move.";
            MainMovePending = false;
            MoveHistory.Clear();
        }

        public void LoadMatchState(Board b, ColorsEnum turn)
        {
            _history.Clear();
            Board = b;
            CurrentTurn = turn;
            StatusMessage = $"Game Loaded. {CurrentTurn} to move.";
            MoveHistory.Clear();
        }

        public bool TakeBack()
        {
            if (_history.Count == 0) return false;

            var snapshot = _history.Pop();
            Board = snapshot.Board;
            CurrentTurn = snapshot.CurrentTurn;
            State = snapshot.State;
            StatusMessage = snapshot.StatusMessage;
            MainMovePending = snapshot.MainMovePending;
            MoveHistory = new List<string>(snapshot.MoveHistory);

            return true;
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

        public List<BoardLocation> GetValidMoves(int q, int r)
        {
            var validMoves = new List<BoardLocation>();
            if (State == GameStateEnum.Finished) return validMoves;

            var piece = Board.AnyoneThere(new BoardLocation(q, r));
            if (piece == null || piece.Color != CurrentTurn) return validMoves;

            if (!MainMovePending)
            {
                var targetPiece = piece.PieceType == PiecesEnum.King ? Board.FindPiece(PiecesEnum.Queen, piece.Color) : 
                                  piece.PieceType == PiecesEnum.Queen ? Board.FindPiece(PiecesEnum.King, piece.Color) : null;
                
                if (targetPiece != null
                    && BoardLocation.IsAdjacent(piece.Location, targetPiece.Location))
                {
                    Board sim = new Board(Board);
                    sim.Remove(piece);
                    sim.Remove(targetPiece);
                    sim.Add(new PlacedPiece(piece.PieceType, piece.Color, targetPiece.Location.Q, targetPiece.Location.R));
                    sim.Add(new PlacedPiece(targetPiece.PieceType, targetPiece.Color, piece.Location.Q, piece.Location.R));
                    
                    var myKing = sim.FindPiece(PiecesEnum.King, CurrentTurn);
                    if (myKing != null && !sim.IsSquareAttacked(myKing.Location, CurrentTurn))
                    {
                        validMoves.Add(targetPiece.Location);
                    }
                }
            }

            var options = Board.WhatCanICauseWithDoo(piece);
            foreach(var eventSet in options)
            {
                bool pieceAdded = false;
                BoardLocation? actualDestination = null;

                foreach(var evt in eventSet)
                {
                    if (evt.EventType == EventTypeEnum.Add && evt.Regarding.PieceType == piece.PieceType)
                    {
                        // By not breaking, we ensure we overwrite the reincarnation coordinate (if any)
                        // with the final piece movement destination, which is always added last.
                        actualDestination = evt.Regarding.Location;
                        pieceAdded = true;
                    }
                }

                if (pieceAdded && actualDestination != null)
                {
                    validMoves.Add(actualDestination);
                }

                // If the piece didn't survive to be added to the board, 
                // its destination MUST have been the Portal void.
                if (!pieceAdded)
                {
                    validMoves.Add(new BoardLocation(0, 0));
                }
            }
            return validMoves;
        }

        public void SubmitMove(int q1, int r1, int q2, int r2)
        {
            if (State == GameStateEnum.Finished) return;

            var piece = Board.AnyoneThere(new BoardLocation(q1, r1));
            if (piece == null) { StatusMessage = "No piece selected."; return; }
            if (piece.Color != CurrentTurn) { StatusMessage = $"It is {CurrentTurn}'s turn!"; return; }

            var preMoveSnapshot = new GameSnapshot(this);

            // --- 1. SWAP LOGIC (Diddilydoo) ---
            var targetPiece = Board.AnyoneThere(new BoardLocation(q2, r2));
            if (targetPiece != null && targetPiece.Color == piece.Color)
            {
                bool isKingQueen = (piece.PieceType == PiecesEnum.King && targetPiece.PieceType == PiecesEnum.Queen);
                bool isQueenKing = (piece.PieceType == PiecesEnum.Queen && targetPiece.PieceType == PiecesEnum.King);

                if (isKingQueen || isQueenKing)
                {
                    if (!BoardLocation.IsAdjacent(piece.Location, targetPiece.Location))
                    {
                        StatusMessage = "King and Queen must be adjacent to Swap.";
                        return;
                    }

                    if (MainMovePending)
                    {
                        // Swap back — player changed their mind
                        Board sim2 = new Board(Board);
                        sim2.Remove(piece);
                        sim2.Remove(targetPiece);
                        sim2.Add(new PlacedPiece(piece.PieceType, piece.Color, q2, r2));
                        sim2.Add(new PlacedPiece(targetPiece.PieceType, targetPiece.Color, q1, r1));

                        var myKing2 = sim2.FindPiece(PiecesEnum.King, CurrentTurn);
                        if (myKing2 != null && sim2.IsSquareAttacked(myKing2.Location, CurrentTurn))
                        {
                            StatusMessage = "You cannot swap back into Check!";
                            return;
                        }

                        _history.Push(preMoveSnapshot);
                        Board.Remove(piece);
                        Board.Remove(targetPiece);
                        Board.Add(new PlacedPiece(piece.PieceType, piece.Color, q2, r2));
                        Board.Add(new PlacedPiece(targetPiece.PieceType, targetPiece.Color, q1, r1));

                        MainMovePending = false;
                        StatusMessage = "Swap reversed. You may Swap again or make your Main Move.";
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

                    _history.Push(preMoveSnapshot);
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
                bool isMatch = false;
                bool pieceAdded = false;

                foreach(var evt in eventSet)
                {
                    if (evt.EventType == EventTypeEnum.Add && evt.Regarding.PieceType == piece.PieceType)
                    {
                        pieceAdded = true;
                        if (evt.Regarding.Location.Q == q2 && evt.Regarding.Location.R == r2)
                        {
                            isMatch = true;
                        }
                    }
                }

                // If the piece vanished (no Add event), the only spot that causes this is the Portal.
                if (!pieceAdded && q2 == 0 && r2 == 0)
                {
                    isMatch = true;
                }

                if (isMatch)
                {
                    isValid = true;
                    validEvents = eventSet;
                    break;
                }
            }
            if (isValid)
            {
                _history.Push(preMoveSnapshot);
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
                    // The attacker vanished. EventsFromAMove already handles the 
                    // destruction of the attacker, so we just append the message.
                    entropyMsg = " (Attacker vanished in the Portal)";
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

                string actionDesc = $"{CurrentTurn}: {piece.PieceType} moves";
                if (captured != null)
                {
                    actionDesc = $"{CurrentTurn}: {piece.PieceType} captures {captured.Color} {captured.PieceType}";
                }
                if (spawnedInPortal)
                {
                    var spawned = Board.AnyoneThere(new BoardLocation(0,0));
                    actionDesc += $", reincarnating a {spawned?.PieceType}";
                }

                actionDesc += entropyMsg;

                if (MainMovePending)
                {
                    MainMovePending = false;
                    AdvanceTurn(actionDesc, preMoveSnapshot);
                }
                else
                {
                    AdvanceTurn(actionDesc, preMoveSnapshot);
                }
            }
            else
            {
                StatusMessage = "Invalid Move.";
            }
        }

        private void AdvanceTurn(string lastAction = "", GameSnapshot preMoveSnapshot = null)
        {
            int currentIdx = TurnOrder.IndexOf(CurrentTurn);
            currentIdx = (currentIdx + 1) % 3;
            CurrentTurn = TurnOrder[currentIdx];
            
            string currentConsequence = CheckVictoryAtStartOfTurn(preMoveSnapshot);
            
            string fullAction = lastAction;
            
            if (State != GameStateEnum.Finished)
            {
                int nextIdx = (TurnOrder.IndexOf(CurrentTurn) + 1) % 3;
                ColorsEnum thirdPlayer = TurnOrder[nextIdx];
                
                string thirdConsequence = GetPlayerStatus(thirdPlayer, preMoveSnapshot);
                
                bool hasCurrent = !string.IsNullOrEmpty(currentConsequence);
                bool hasThird = !string.IsNullOrEmpty(thirdConsequence);

                if (hasCurrent && hasThird)
                {
                    fullAction += $", {currentConsequence} and {thirdConsequence}";
                }
                else if (hasCurrent)
                {
                    fullAction += $", {currentConsequence}";
                }
                else if (hasThird)
                {
                    fullAction += $", {thirdConsequence}";
                }

                fullAction += ".";
            }
            else
            {
                if (!string.IsNullOrEmpty(currentConsequence))
                {
                    fullAction += $", {currentConsequence}";
                }
                fullAction += "!";
            }

            MoveHistory.Add(fullAction);
            if (MoveHistory.Count > 2)
            {
                MoveHistory.RemoveAt(0);
            }

            StatusMessage = string.Join("\n", MoveHistory);
        }

        private bool WasInCheck(GameSnapshot preMoveSnapshot, ColorsEnum victimColor)
        {
            if (preMoveSnapshot == null) return false;
            var king = preMoveSnapshot.Board.FindPiece(PiecesEnum.King, victimColor);
            if (king == null) return false;
            return preMoveSnapshot.Board.GetAttackingColors(king.Location, victimColor).Any();
        }

        private string CheckVictoryAtStartOfTurn(GameSnapshot preMoveSnapshot)
        {
            var attackers = GetAttackers(CurrentTurn);
            if (!attackers.Any()) return ""; 

            string verb = WasInCheck(preMoveSnapshot, CurrentTurn) ? "leaving" : "putting";

            if (CanEscape(Board, CurrentTurn))
            {
                return $"{verb} {CurrentTurn} in Check";
            }

            State = GameStateEnum.Finished;

            // Check if the victim was ALREADY in checkmate before the most recent move
            bool wasAlreadyInCheckmate = preMoveSnapshot != null && 
                                         WasInCheck(preMoveSnapshot, CurrentTurn) && 
                                         !CanEscape(preMoveSnapshot.Board, CurrentTurn);

            int myIdx = TurnOrder.IndexOf(CurrentTurn);
            int prevIdx = (myIdx + 2) % 3; // The player who just moved
            int prevPrevIdx = (myIdx + 1) % 3; // The player who moved before them

            // If they were already in checkmate, the player before last created it. 
            // Otherwise, the player who just moved created it.
            ColorsEnum winner = wasAlreadyInCheckmate ? TurnOrder[prevPrevIdx] : TurnOrder[prevIdx];
            
            // Fix: Only call it a Priority Checkmate if the mate survived a third player's turn
            if (wasAlreadyInCheckmate)
            {
                return $"resulting in {winner} winning by Priority Checkmate";
            }
            else
            {
                return $"resulting in {winner} winning by Checkmate";
            }
        }

        private string GetPlayerStatus(ColorsEnum color, GameSnapshot preMoveSnapshot)
        {
            var attackers = GetAttackers(color);
            if (!attackers.Any()) return ""; 

            string verb = WasInCheck(preMoveSnapshot, color) ? "leaving" : "putting";

            if (CanEscape(Board, color))
            {
                return $"{verb} {color} in Check";
            }
            else
            {
                return $"{verb} {color} in GRAVE DANGER";
            }
        }

        /// <summary>
        /// Returns the colors that are currently attacking the given player's King.
        ///
        /// FIX: Uses Board.GetAttackingColors (which delegates to WhereCanIReach)
        /// instead of WhatCanICauseWithDoo. The latter deliberately skips moves
        /// whose destination contains a King (to prevent king-capture as a legal
        /// move), which caused GetAttackers to return an empty list even when
        /// the king was clearly under attack — breaking checkmate detection.
        /// </summary>
        private List<ColorsEnum> GetAttackers(ColorsEnum victimColor)
        {
            var king = Board.FindPiece(PiecesEnum.King, victimColor);
            if (king == null) return new List<ColorsEnum>();

            return Board.GetAttackingColors(king.Location, victimColor);
        }

        private bool CanEscape(Board board, ColorsEnum victimColor)
        {
            var myPieces = board.PlacedPieces.Where(p => p.Color == victimColor).ToList();

            foreach(var p in myPieces)
            {
                var outcomes = board.WhatCanICauseWithDoo(p);
                foreach(var eventSet in outcomes)
                {
                    Board simBoard = new Board(board);
                    
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