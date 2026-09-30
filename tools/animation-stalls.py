# Measures frame stalls while pieces slide, and whether the turn shimmer fires mid-slide.
#   python3 tools/animation-stalls.py http://localhost:5299
# Prints total stalled ms and the worst frame gap over a Blue AI move plus the two AI replies,
# then repeats with 300 ms of emulated network latency and reports when the shimmer fired
# relative to the slide (positive = still sliding = the 2026-09-30 mid-slide pause).
import sys, time, urllib.request
from playwright.sync_api import sync_playwright
U = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5299"
def run(gid, latency=0):
    with sync_playwright() as p:
        b = p.chromium.launch(executable_path="/root/.cache/ms-playwright/chromium_headless_shell-1208/chrome-headless-shell-linux64/chrome-headless-shell", args=["--no-sandbox","--disable-gpu"])
        pg = b.new_page(viewport={"width":412,"height":915}, is_mobile=True)
        if latency:
            cdp = pg.context.new_cdp_session(pg); cdp.send("Network.enable")
            cdp.send("Network.emulateNetworkConditions", {"offline": False, "latency": latency, "downloadThroughput": -1, "uploadThroughput": -1})
        pg.goto(f"{U}/?game={gid}"); pg.wait_for_selector("#pieces-group > g"); pg.wait_for_timeout(6000)
        pg.evaluate("""() => { window.__gaps = []; window.__shim = []; let last = performance.now();
            (function raf() { const n = performance.now(); if (n - last > 50) window.__gaps.push(Math.round(n - last)); last = n; requestAnimationFrame(raf); })();
            const orig = window.shimmerPieces; window.shimmerPieces = c => { window.__shim.push(animationBusyUntil - Date.now()); orig(c); }; }""")
        urllib.request.urlopen(urllib.request.Request(f"{U}/Game/ai-move?gameId={gid}&forColor=Blue", method="POST")).read()
        pg.wait_for_timeout(14000)
        gaps, shim = pg.evaluate("[window.__gaps, window.__shim]"); b.close()
    return gaps, shim
tag = time.strftime("%H%M%S").translate(str.maketrans("0123456789", "abcdefghij"))
g, shim = run("stall" + tag)
print(f"no latency : stalled {sum(g)} ms total, worst gap {max(g) if g else 0} ms; shimmer fired at slide-remaining {shim} ms")
g, shim = run("stallslow" + tag, latency=300)
print(f"300ms lat. : stalled {sum(g)} ms total, worst gap {max(g) if g else 0} ms; shimmer fired at slide-remaining {shim} ms  {'<- MID-SLIDE' if any(x > 0 for x in shim) else '(after the slide)'}")
