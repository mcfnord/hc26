# PLAN.md — HexC on johns.living

Living plan. Update it as decisions are made. Nothing here is built yet unless
marked **[done]**.

## The vision (as stated 2026-09-27)

- https://johns.living shows a playable HexC game, great on an Android phone.
- First iteration: a human plays against AI opponents. AI opponents have
  named identities (at least two) so they can sit on a leaderboard.
- ELO-style rating with explicit, published rules; all-time leaderboard.
- Google sign-in.
- Visitors without a game see the game in progress (the only one, or the most
  active one). Signed-in players with a game land in it automatically.
- Multi-game management stays as unobtrusive as possible.
- Eventually a PWA with turn notifications.
- **Rollback is a permanent feature**: "revert" must be a one-word request
  that visibly undoes the last deploy.
- Local headless-browser testing so changes are verified before deploy.

## Decisions since (newest first)

- **2026-09-30 — Google sign-in is next.** The operator calls the game
  playable and wants other people to start playing almost right away by
  signing in with Google. Proposed order, one deploy each: (1) sign-in button,
  server validates the Google ID token and sets its own cookie session, page
  shows who is signed in, with nothing else changed; (2) each signed-in player gets
  their own game vs the two AIs, keyed by their user id, and visitors who
  aren't signed in watch; (3) persistence, because with several players a deploy
  wiping every game becomes a real cost. Blocked on the operator creating
  the OAuth client ID (see Risks).
- **2026-09-30 — Animation pacing (operator).** A captured piece slides to
  the graveyard twice as fast as before, and a reincarnating piece slides into
  the portal *at the same time* (this reverses the earlier "sequential" wish).
  A normal move is 20% faster with a shorter slow-down at the end. The turn
  shimmer is 0.45s and hits all pieces at once. An open page reloads itself
  after a deploy.
- **2026-09-28 — Heavy investment in testing and polish.** The operator's
  experience is that AI-made GUIs regress and grow side effects easily, and
  that rule slips would be terrible. So every detail the operator reports
  should become (1) a reproducible scenario (a seeded position plus moves),
  (2) a headless video with frame sheets to discuss, and (3) a regression
  test. Rule bugs go to engine tests. Animation bugs go to tests on the
  animation's event order. Proven the same day: headless Playwright records
  webm, and ffmpeg turns it into timestamped frame sheets Claude can view.
- **2026-09-28 — Single operator game, no sign-in yet.** Google auth is
  deferred. Anyone who opens https://johns.living is assumed to be the
  operator, playing **Blue** against two AIs (White, Red) in one shared game
  (server game ID `main`). The page auto-joins it; a "New Game" button resets
  it. `?game=ID` in the URL joins a different game (used by the UI tests).
  The AI is driven by the open browser tab: it polls status and calls
  `ai-move` for White/Red, passing `forColor` so a second tab can't double-move.
  The game is in memory only; a deploy or restart loses the position
  (persistence remains Phase 2). Phases 2–3 below are reordered accordingly:
  play-vs-AI ships first without named bots; sign-in comes when it is needed
  to tell two humans apart.

- **2026-09-28 — First phone feedback (operator, Android).** Board was too
  narrow in portrait and cut off in landscape; graveyard ("off board" area)
  not visible in landscape. Fixed: tighter SVG viewBox, board container may
  shrink, and in landscape the graveyard is a column beside the board.
  Operator notes to carry forward: they will trim the top buttons themselves;
  they **like the narrative** (the move-by-move status text) and want to keep
  it; players need a way to **step back and see the moves made** without
  undoing them (Review mode already replays the timeline; whether it is the
  right shape for a phone is an open question, see below).

## What is on this box today (2026-09-27)

- DigitalOcean droplet, Ubuntu 24.04, **1 vCPU, 961 MB RAM**, 16 GB disk free.
- `johns.living` → 143.198.104.1 (this box). nginx on port 80 only serves the
  static "For Chloe" site from `/var/www/html`. **No TLS yet.**
- Not installed: dotnet, node, chromium, pwsh. Playwright's UI tests can't run
  until dotnet + a browser exist.
- Two `claude` processes use ~600 MB. That leaves ~200 MB. A .NET server is
  fine (~80–120 MB); a headless Chromium alongside it is not. See Risks.

## Architecture decisions

1. **Keep the C# engine and server.** The rules are already encoded and tested.
   Everything new (auth, ratings, game directory) is added around `Game`, not
   inside it.
2. **nginx in front, Kestrel behind.** nginx terminates TLS (Let's Encrypt via
   certbot) and proxies `/` to the HexC server on 127.0.0.1:5235. "For Chloe"
   moves to `/chloe/` or is retired; user decides.
3. **Persistence: SQLite** in `/var/lib/hexc/hexc.db`. One file, easy to back
   up, easy to roll back together with the code. Tables: users, games (with
   full move list so a game can be replayed/reloaded on restart), ratings,
   rating_history.
4. **Deploy = git tag + systemd + symlink flip.**
   - `deploy.sh` builds `HexC.Server` into `/opt/hexc/releases/<git-sha>/`,
     copies the DB to `/opt/hexc/backups/<sha>.db`, points
     `/opt/hexc/current` at the new release, restarts `hexc.service`, then
     runs a smoke test (curl `/healthz`, create a game, make an AI move).
     If the smoke test fails, it reverts itself.
   - `revert.sh [n]` flips the symlink back n releases (default 1), restores
     the matching DB backup, restarts. That is what "revert" means.
   - Everything is also a git commit, so code history and deploy history match.
5. **Auth: Google Identity Services (one-tap / sign-in button).** The browser
   gets an ID token from Google; the server validates it and issues its own
   cookie session. No passwords stored. Requires HTTPS and a Google Cloud
   OAuth client ID (user must create it in Google Cloud Console and give the
   client ID; nothing else needed).
6. **Identity of players.** A `Player` is either a signed-in human or a named
   bot. Bots are rows in the same table so the leaderboard treats them alike.
7. **Rating: standard Elo, K=32 for < 30 games, K=16 after.** Three-player
   games are scored as three pairwise results (1st beats 2nd and 3rd, 2nd
   beats 3rd). Everyone starts at 1200. Rules will be published on
   `/ratings` in plain language. Draws/abandonments: TBD, decide before the
   first rated game.
8. **Testing tiers**
   - Engine + API xUnit tests: run on every change (fast, in-process).
   - Playwright with **mobile viewport emulation** (Pixel-class device
     descriptor) against the local server: run before every deploy. Needs
     ~300 MB free while it runs; see Risks.
   - Manual: user plays on the phone and reports.

## Risks / things the user must decide

- **RAM.** 1 GB is tight for Kestrel + Chromium + two Claude sessions. Options:
  resize the droplet to 2 GB (cheapest fix), or run Playwright only when the
  Claude sessions are idle, or add a 2 GB swapfile (slow but unblocks it).
  DECIDED 2026-09-27: 2 GB swapfile now; resize when the game is live.
- **"For Chloe" site**: DECIDED 2026-09-27: retire it. Files stay in
  `/var/www/html`; HexC takes the root.
- **Google OAuth client ID**: user creates it (Cloud Console → APIs & Services
  → Credentials → OAuth client → Web application; authorized origin
  `https://johns.living`). Paste the client ID; it is not a secret.

## Phases (each one is deployable and revertible)

0. **Infra** — install dotnet 8 SDK, swapfile, certbot + TLS, nginx proxy,
   `hexc.service`, `deploy.sh` / `revert.sh`, `/healthz`. Deploy the game as
   it exists today to https://johns.living. Prove `revert` works.
   **[done 2026-09-28]** first deploy = single-game mode (see Decisions).
   `revert` needs a second release to exist before it can be proven.
1. **Phone-first UI pass** — viewport meta, touch targets, board sizing to
   width, no hover-dependent affordances. Playwright mobile test for "board
   renders and a move can be made by tapping".
2. **Play vs AI with named bots** — two bot identities; a "New game vs X and
   Y" flow; game persists in SQLite; server restart reloads games from move
   lists.
3. **Google sign-in** — session cookie; games owned by a user; landing logic
   (your game → most active game → new game).
4. **Ratings + leaderboard** — Elo per rules above; `/ratings` page.
5. **PWA** — manifest, service worker, installable; then Web Push for
   "your turn" notifications.
   **[partly done 2026-09-28]** manifest + icons, `display: standalone`, so
   "Add to Home screen" in Chrome runs it without the URL bar. No service
   worker yet (not needed for install; needed later for offline + push).
6. **Multiple humans** — invite links, spectators. Later.

## Open questions

- Should the AI move instantly, or with a small delay so the phone shows the
  human's move settle first? (UI already has an AI auto-play mode; check how
  it feels on the phone.)
- Game abandonment: after how long does an unfinished game stop being "the
  most active game"?
- Attacking a piece in the Portal: both vanish, then reincarnation returns a
  piece of the *victim's* type to the attacker's side on the portal. Pawn takes
  Pawn there thus ends with a same-colour Pawn on the portal, which looks like
  the attacker survived (operator noticed 2026-09-28; engine test
  `AttackIntoPortal_BothVanish_ReincarnationOfVictimTypeLandsOnPortal`). Is that
  the intended rule, or should attacking into the portal never reincarnate?
- "Step back and see the moves" on a phone: is the existing Review mode
  (scrub through the timeline) enough, or should the narrative become a
  scrollable move list that highlights the move on the board when tapped?
