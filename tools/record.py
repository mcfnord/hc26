import sys, time, json, urllib.request, glob, os
from playwright.sync_api import sync_playwright
url, out, secs = sys.argv[1], sys.argv[2], int(sys.argv[3])
with sync_playwright() as p:
    b = p.chromium.launch(executable_path="/root/.cache/ms-playwright/chromium_headless_shell-1208/chrome-headless-shell-linux64/chrome-headless-shell", args=["--no-sandbox","--disable-gpu","--disable-dev-shm-usage"])
    ctx = b.new_context(viewport={"width":412,"height":915}, is_mobile=True, has_touch=True, record_video_dir=out, record_video_size={"width":412,"height":915})
    pg = ctx.new_page()
    pg.goto(f"{url}/?game=vid")
    pg.wait_for_selector('#turn-indicator:has-text("Turn")', timeout=20000)
    pg.wait_for_timeout(2000)
    urllib.request.urlopen(urllib.request.Request(f"{url}/Game/ai-move?gameId=vid&forColor=Blue", method="POST")).read()
    pg.wait_for_timeout(secs*1000)
    ctx.close(); b.close()
