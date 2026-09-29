import sys
from playwright.sync_api import sync_playwright
url = sys.argv[1]; out = sys.argv[2]
with sync_playwright() as p:
    b = p.chromium.launch(executable_path="/root/.cache/ms-playwright/chromium_headless_shell-1208/chrome-headless-shell-linux64/chrome-headless-shell", args=["--no-sandbox","--disable-gpu","--disable-dev-shm-usage"])
    for name, w, h in [("portrait",412,915),("landscape",915,412)]:
        pg = b.new_page(viewport={"width":w,"height":h}, device_scale_factor=1, is_mobile=True, has_touch=True)
        pg.goto(f"{url}/?game=shot")
        pg.wait_for_selector("#turn-indicator:has-text(\"Turn\")", timeout=20000)
        pg.wait_for_timeout(1500)
        pg.screenshot(path=f"{out}/{name}.png")
        # board geometry: the svg box and the bounding box of the hex group
        info = pg.evaluate("""() => { const s=document.getElementById('hex-board').getBoundingClientRect();
            const g=document.querySelector('#hex-board g') ; const gb=g?g.getBoundingClientRect():null;
            const tb=document.getElementById('top-bar').getBoundingClientRect();
            return {svg:[s.x|0,s.y|0,s.width|0,s.height|0], hexes: gb&&[gb.x|0,gb.y|0,gb.width|0,gb.height|0], topbar:tb.height|0, inner:[innerWidth,innerHeight]} }""")
        print(name, info)
        pg.close()
    b.close()
