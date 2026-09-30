# TODO

- Table games are only driven while a page is open: the page moves the AI seats
  (as for anonymous games). If it is an AI seat's turn and nobody has the game
  open, it waits until someone does. Fine for now; the server clock could take
  it over later.
- The `?game=ID` URL parameter is ignored when signed in (the lobby decides).

- No automated test yet for animation order (PLAN.md 2026-09-28 testing decision).
  The portal-attack sequence (d9cc631) and the simultaneous capture/reincarnation
  slide (c672f85) were checked by hand with headless Playwright, sampling piece
  positions after calling renderPieces() with a hand-made boardState and lastMove.
  That technique could become the first animation-order test.

- ~~`POST /Game/create` said "White to move"~~ fixed 2026-09-28; message now derives from `game.CurrentTurn`.
- Operator plans to trim the top-bar buttons (Review/Export/Undo/New Game) on the phone layout.
- ~~Every deploy restarts the server and loses the in-memory game.~~ Fixed 2026-09-30: games are stored as move lists in SQLite (`/var/lib/hexc/hexc.db`) and replayed at startup.

## Paused 2026-09-29 (operator rebooting). Resume here.
- **Bug seen in a headless video:** the reincarnating piece sits visible
  below the board for about 10s (not in its dead box, not hidden), then
  slides to the portal. It should wait in the dead box and slide to the
  center only after the victim has finished sliding off.
  (2026-09-30: superseded. The operator now wants the two slides to run at the
  same time, and that shipped in c672f85. Check whether the "sits below the
  board" part still happens.) The operator also
  says the rook-takes-rook case is "still wrong, but on the right track".
  They haven't described exactly what they saw.
- **Next step, proposed but not yet approved:** a scenario loader (a
  test-only seeded position plus moves) for rook-takes-rook with the
  portal, record it, fix the bug above, and add the first test on animation
  event order. See the 2026-09-28 testing decision in PLAN.md.
- **Tools:** `tools/animation-stalls.py <url>` measures frame stalls during slides
  and whether the turn shimmer fires mid-slide (the 2026-09-30 "pause mid-slide"
  bug: the shimmer's filter animation ran during the slide on a slow connection).
  Run it against a local server before changing animations or CSS.
  `tools/record.py <url> <outdir> <secs>` records a webm with
  headless Playwright. Start the local server first on :5299. Frame sheets:
  `ffmpeg -i x.webm -vf "fps=1,scale=206:-1,drawtext=text='%{pts\:hms}':x=4:y=4:fontsize=14:fontcolor=yellow:box=1:boxcolor=black,tile=8x2" sheet%d.png`.
  `tools/screenshot.py <url> <outdir>` takes portrait and landscape shots.
