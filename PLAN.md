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

- **2026-10-17 — PROPOSED, awaiting operator approval: Vision 2, "play your
  friends over many games, and climb".** Supersedes the step order of
  2026-10-05 once approved (alerts stay; steps 2–4 fold into the milestones
  below).

  *How we work (operator, 2026-10-17):* set the vision, then move fast through
  it, **one approved step at a time**. Claude builds and tests each step on a
  branch (not `main`), the operator runs it on its **own droplet**, separate from
  johns.living, confirms it, and approves the next step. johns.living stays as
  it is until the operator decides to move it over.

  *The promise to a player:* you can play HexC with your friends, every game
  you play is kept and can be replayed, and you have a rating you try to raise.

  **M0. A safe place to build fast.** Done when the branch runs on its own
  droplet, a deploy can be reverted without losing games, and the full test
  suite, browser tests included, passes in Claude's container.
  - 0.1 Serve anime.js from `wwwroot`, not a CDN (the board breaks without it).
  - 0.2 Simulated sign-in (operator, 2026-10-17: no Google on the test
    instance). There you sign in by typing a name; it is clearly marked as a
    test server. The same sign-in lets browser tests seat three named players.
    Real Google sign-in stays on johns.living.
  - 0.3 Backups that really contain the games (`sqlite3 .backup`; WAL-safe),
    checked by the smoke test.
  - 0.4 Deploy scripts take the host and domain as settings. Operator creates
    the droplet and a DNS name for it (no Google setup needed, see 0.2).

  **M1. Games are kept.** Done when a player can find and replay any game
  they finished.
  - 1.1 Store the result when a game ends: who won, how, when. Fill it in for
    old games by replaying them.
  - 1.2 "My games": your finished games (date, players, winner); tap one to
    open it in Review.
  - 1.3 A link to a finished game that anyone can open to watch the replay.

  **M2. Play with friends.** Done when three friends can start a game
  together and play another one straight after.
  - 2.1 Invite link: start a private table and share it; empty seats can go
    to AI.
  - 2.2 Rematch: the same players again, colours rotated.
  - 2.3 More than one unfinished game at a time, with a small game switcher.

  **M3. Ratings.** Done when every finished human game moves ratings, by
  published rules, and a leaderboard shows them.
  - 3.1 **[decided 2026-10-17]** The rules: brutally simple (operator chose
    them over a harsher, more complex dropout penalty). This is the whole
    rulebook:
    1. Everyone starts at 1200. Only games between signed-in players at a
       table count.
    2. The winner beats both other players; the other two draw with each
       other. Same for checkmate and Ascension.
    3. If the AI is playing your seat when the game ends, you lose to every
       player who wasn't.
    Ratings move by standard Elo with one fixed step (K=32 to start). Resign
    is a button that hands your seat to the AI for the rest of the game, so
    rule 3 covers it. A short absence you return from costs nothing. No
    counters, windows or extra penalties. If quitting becomes a problem, show
    a completion record ("finished 47 of 50") rather than add a rule.
  - 3.2 (proposed) The server plays AI seats itself, so every game reaches an
    end and stalled games need no rule.
  - 3.3 (proposed) Named bots, each with one trait (e.g. Rusher, Butcher,
    Turtle, Gambler, Hunter), always shown with a 🤖 beside the name, each
    with its own rating like any player. Beating a bot rated above you raises
    yours (operator, 2026-10-17). A signed-in player alone can fill the empty
    seats with bots (changes the 2026-09-30 "alone never plays AI" rule). The
    stand-in that plays an absent person's seat is not a bot and has no
    rating; that seat's result is the person's (rule 3).
  - 3.4 Elo from stored results; your rating beside your name.
  - 3.5 Leaderboard and a plain-language `/ratings` rules page.

  **M4. Coming back.** Done when a player is told it is their turn without
  having the page open.
  - 4.1 Service worker and Web Push: "your turn".
  - 4.2 Profile: rating history, record against each friend.

- **2026-10-05 — Next goal: a stranger shows up and finishes a game with the
  operator.** The operator wants to get strong at HexC by playing humans, and
  will want a rating system that challenges them later. The order, one step each:
  1. **Operator alerts [done 2026-10-05].** The server pushes to the
     operator's phone through ntfy.sh (chosen over Web Push and email as the
     smallest step). The secret topic and operator id are in
     `/etc/hexc/alerts.env`, not git. Alerts: (a) someone other than the
     operator sits down at a table, and (b) *watches*: positions linked to a
     past bug, checked on every accepted move (`Watches` in `alerts.cs`), so a
     fix can be seen live. The first watch is the portal "twin on origin"
     (1ec585f). Every alert is also logged (`journalctl -u hexc | grep ALERT`).
  2. Make "someone has arrived" to "operator is playing them" fast. The alert
     link lands the operator at the waiting table. The 60s countdown still lets
     AI take the third seat, so the two of them can start right away.
  3. A careful eye on active games: an operator view of games in progress and
     their outcomes, with suspicious positions flagged.
  4. Ratings (Phase 4), once real games between humans are finishing.
  The operator's standing rule continues: every failure gets a guard so it
  never returns (a regression test, and a watch when it can only be seen live).

- **2026-09-30 — Absence rule: one grace period, then the AI plays your seat
  until you're back.** Replaces the flat one-hour turn clock. A person whose
  turn it is gets **15 minutes**. If they don't move, the AI moves for them and
  their seat is *on AI*: the AI moves at once on each of their turns, so the
  other two never wait. They are **back** when they have touched the page (tap,
  click, key, or bringing the tab to the front) within the last **5 minutes**,
  or when they move by hand. Back takes the seat off AI and restores the full
  grace on each turn. An open tab nobody touches does not count as present, so
  a browser left running on a desk stops delaying the game after five minutes.
  Both numbers are constants in `GameStore` (`TurnClock`, `Presence`). The
  title stays clean: just "Your Turn" or "Bob's Turn" in the player's colour
  (operator, 2026-09-30). No clock in it; a two-minute warning may come later.
  The status endpoint still exposes `aiStepsInUtc` and `onAi` for that. On-AI state is in memory only: a server restart
  gives everyone a fresh grace, the safe side. Considered and rejected: an
  escalating 60/30/15 clock (a forfeit in disguise, more state, same outcome)
  and pausing the clock while present (an idle tab would stall the game).
  Later, when ratings exist, moves the AI made for someone must not count as
  theirs.

- **2026-09-30 — Handicap pawns: never built.** Early on there was an idea of
  giving a weaker player extra Pawns as a handicap. Nothing in the engine or
  server supports it: the graveyard is the fixed starting set minus the board,
  so reincarnation can never raise a type above its starting count (3 Pawns).
  Not planned. Recorded so the README doesn't hint at a fourth Pawn again.

- **2026-09-30 — Landing flow: zero buttons.** The operator wants the
  simplest possible thing with no choices. Decided:
  - Signed out: open the site and you are playing two AIs at once (as today).
    That game is a throwaway.
  - Sign in with an unfinished game: you land in it.
  - Sign in with no unfinished game: you are seated at the one waiting table
    (refined 2026-09-30, later the same day). The first person waits with no
    countdown, for as long as it takes. When a second person arrives, a
    server-side countdown starts for the third seat; a third human who signs
    in before it ends takes the seat, otherwise AI does and the game starts.
    The countdown runs whether or not any tab is open. Seats are Blue, White,
    Red in arrival order. A signed-in person alone never plays AI (sign out
    for that). The countdown length is one constant (start at 60s).
  - **Turn clock (decided 2026-09-30): 1 hour.** If it has been a human's
    turn at a table for an hour, the AI makes one move for them, so an absent
    player never blocks the others. Nothing is cancelled; a returning player
    finds their game further along. Runs on the server (`LobbyClock`).
  - **Shipped 2026-09-30** (`GameStore.Enter/Leave/Tick`, `/Lobby/enter`,
    `/Lobby/leave`, seat checks on `/Game/move`, `ai-move`, `undo`, `reset`).
    Anonymous play stays on the shared game `main`, unchanged.
  - Signing in does NOT adopt the anonymous game you were in; it is discarded.
  - This supersedes the earlier "signed-in with no game → choose AI or table"
    idea and the 2026-09-30 sign-in step 2 above.
  Order: persistence first (games between humans must survive a deploy), then
  this flow. Step 1 of sign-in (button + cookie session) shipped in 3b1c324.
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
7. **Rating:** superseded 2026-10-17 by the three simple rules in Vision 2,
   step 3.1. (Was: K=32 then 16, pairwise 1st/2nd/3rd placings, which HexC
   doesn't produce: a game has one winner.)
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
   lists. **[persistence done 2026-09-30]** `GameRepository` stores each
   accepted move; `GameStore.Rebuild` replays them at startup. Named bots
   still to do.
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

- Ratings and the turn clock: when the AI moves for an absent player, how does
  that count for their rating? **Answered 2026-10-17:** the seat's result is
  the person's; if the AI is playing it when the game ends, they lose to every
  player who wasn't (Vision 2, step 3.1, rule 3).

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
  **Answered 2026-09-30:** the operator keeps the rule ("I sorta get it") but
  the animation must show it: the attacker slides into the portal, both pieces
  slide to the graveyard, and only then does the reincarnated piece slide
  onto the portal. Shipped in d9cc631.
- "Step back and see the moves" on a phone: is the existing Review mode
  (scrub through the timeline) enough, or should the narrative become a
  scrollable move list that highlights the move on the board when tapped?
