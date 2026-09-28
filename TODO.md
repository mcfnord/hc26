# TODO

- ~~`POST /Game/create` said "White to move"~~ fixed 2026-09-28; message now derives from `game.CurrentTurn`.
- Operator plans to trim the top-bar buttons (Review/Export/Undo/New Game) on the phone layout.
- Every deploy restarts the server and loses the in-memory game. The page now recreates it, but the position is gone. Persistence (PLAN.md Phase 2) is the real fix.
