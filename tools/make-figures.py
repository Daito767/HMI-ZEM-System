"""Render the images used by the README and by the operator manual.

    python tools/make-figures.py

Nothing here is drawn by hand. There are two kinds of figure, and both are made
out of the application itself:

  * the stand, composed from the very SVG layers the application loads from
    `wwwroot/cell`, stacked in the order `TopView` and `FrontView` stack them,
    with pallets at the positions `StandGeometry` computes;
  * the interface, rendered with the application's own `app.css` and with markup
    copied from the Razor components, so it looks exactly like the screen.

They are renders rather than captures of a running window, but they go through
the same stylesheet in the same engine family the app's WebView uses.

Output: `docs/images/*.png`, cropped to content. Needs `pip install Pillow` and
Microsoft Edge.
"""
import subprocess
import sys
import tempfile
import time
from pathlib import Path

HERE = Path(__file__).parent.resolve()
ROOT = HERE.parent
WWW = ROOT / "ZEM_BoschRexrothSystemByASTI" / "wwwroot"
CELL = WWW / "cell"
APP_CSS = WWW / "app.css"
OUT = ROOT / "docs" / "images"

EDGE_CANDIDATES = [
    Path(r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"),
    Path(r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"),
]

PANEL = "#171c26"
SCALE = 2  # device pixel ratio, so text in the figures survives printing

# --- stand geometry, the same numbers as in StandGeometry ---------------------

TOP_W, TOP_H = 750.0, 900.0
FRONT_W, FRONT_H = 750.0, 500.0
COLUMN_SHIFT = 140.0
ARM_BASELINE = 266.0
GRIPPER_ROW = 490.0
PULLER_REACH = 124.0
COLUMN_FRONT = GRIPPER_ROW - PULLER_REACH   # 366, as in StandGeometry
PALLET_DEPTH = 118.0


def move(x_mm: float, y_mm: float, w_mm: float, h_mm: float) -> str:
    return f"transform:translate({x_mm / w_mm * 100:.3f}%,{y_mm / h_mm * 100:.3f}%)"


def top(x_mm: float, y_mm: float) -> str:
    return move(x_mm, y_mm, TOP_W, TOP_H)


def front(x_mm: float, y_mm: float) -> str:
    return move(x_mm, y_mm, FRONT_W, FRONT_H)


def layer(view: str, name: str, style: str = "", cls: str = "") -> str:
    uri = (CELL / view / name).as_uri()
    style = f' style="{style}"' if style else ""
    cls = f" {cls}" if cls else ""
    return f'<img class="layer{cls}" src="{uri}" alt=""{style}>'


def stand_page(stack: list[str], ratio: str) -> str:
    return f"""<!DOCTYPE html><html><head><meta charset="utf-8"><style>
html,body{{margin:0;background:{PANEL};overflow:hidden;}}
.stand{{position:relative;width:100%;aspect-ratio:{ratio};}}
.stand .layer,.stand .pallet{{position:absolute;inset:0;width:100%;height:100%;}}
.stand .layer{{display:block;object-fit:contain;}}
.stand .layer.see-through{{opacity:.55;}}
</style></head><body><div class="stand">{"".join(stack)}</div></body></html>"""


def stand_top() -> str:
    def pallet(x_mm: float, y_mm: float, pieces: list[str]) -> str:
        inner = layer("top", "pallet.svg")
        inner += "".join(layer("top", f"piece-{p}.svg") for p in pieces)
        return f'<div class="pallet" style="{top(x_mm, y_mm)}">{inner}</div>'

    stack = [
        layer("top", "base-rails.svg"),
        layer("top", "puller-left.svg"),
        layer("top", "puller-right.svg"),
        layer("top", "puller-left-piston.svg"),
        layer("top", "puller-right-piston.svg"),
        # The positions are the ones StandGeometry computes, not eyeballed: one
        # pallet arrived at the front of the belt, two queued in the left column,
        # one in the right. `forward` grows towards the front of the cell.
        pallet(0, GRIPPER_ROW, ["lower-left-red", "lower-right-green", "upper-left-white"]),
        pallet(-COLUMN_SHIFT, COLUMN_FRONT, ["lower-left-cyan", "upper-right-orange"]),
        pallet(-COLUMN_SHIFT, COLUMN_FRONT - PALLET_DEPTH, ["upper-left-black"]),
        pallet(COLUMN_SHIFT, COLUMN_FRONT, []),
        layer("top", "gate-upper-up.svg"),
        layer("top", "gate-upper-down.svg"),
        layer("top", "gate-lower-left.svg"),
        layer("top", "gate-lower-right.svg"),
        layer("top", "storage-rails.svg"),
        layer("top", "arm-rails.svg", cls="see-through"),
        layer("top", "arm.svg", top(ARM_BASELINE, 0), cls="see-through"),
    ]
    return stand_page(stack, "750/900")


def stand_front() -> str:
    head = front(ARM_BASELINE, 0)
    stack = [
        layer("front", "metal-rail-back.svg"),
        layer("front", "vertical-rails.svg"),
        layer("front", "arm-rails.svg"),
        # Back wall of the pallet on the belt, then the head, then the front
        # wall: the order FrontView uses, so the head is seen reaching into it.
        layer("front", "pallet-back-shadow.svg"),
        layer("front", "pallet-back.svg"),
        layer("front", "vacuum.svg", head),
        layer("front", "gripper.svg", head),
        layer("front", "gripper-claw-left.svg", head),
        layer("front", "gripper-claw-right.svg", head),
        layer("front", "pallet-fore-shadow.svg"),
        layer("front", "pallet-fore.svg"),
        layer("front", "vertical-pistons.svg", head),
        layer("front", "horizontal-rails.svg"),
    ]
    return stand_page(stack, "750/500")


# --- the interface, with the real app.css -------------------------------------

MARK = """<svg class="mark" viewBox="0 0 100 100" aria-hidden="true">
<rect x="2" y="2" width="44" height="44" rx="8" fill="#990000" fill-opacity="0.3"
      stroke="#b0343a" stroke-width="2.5" stroke-dasharray="5 4"/>
<path d="M17.8,17.3 A6.2,6.2 0 0 1 30.2,17.3 C30.2,23.5 24,23.5 24,28.3"
      fill="none" stroke="#e8888c" stroke-width="3.3" stroke-linecap="round"/>
<circle cx="24" cy="34.5" r="2.4" fill="#e8888c"/>
<rect x="54" y="2" width="44" height="44" rx="8" fill="#FFFFFF"/>
<rect x="2" y="54" width="44" height="44" rx="8" fill="#FFFFFF"/>
<rect x="54" y="54" width="44" height="44" rx="8" fill="#FF0000"/>
</svg>"""


def ui_page(body: str, width: int | None, bg: str = PANEL, extra: str = "") -> str:
    sizing = f"body{{width:{width}px;}}" if width else ""
    return f"""<!DOCTYPE html><html><head><meta charset="utf-8">
<link rel="stylesheet" href="{APP_CSS.as_uri()}">
<style>
html,body{{margin:0;background:{bg};overflow:hidden;}}
{sizing}
/* The figures are stills: a blinking led would be caught at a random moment. */
*{{animation:none !important;}}
{extra}
</style></head><body>{body}</body></html>"""


def top_bar(alarm: bool, running: bool) -> str:
    """The bar as MainLayout renders it. Kept apart: two figures use it."""
    mode = "AUTO" if running else "MANUAL"
    alarm_pill = (
        '<button class="pill bad alarm sounding"><span class="led"></span>'
        '<span class="value">FARA AER IN SISTEM</span></button>' if alarm else ""
    )
    return f"""<header class="hmi-top">
  {MARK}
  <div class="name">HMI ZEM SYSTEM</div>
  <span class="pill {"ok" if running else ""}"><span class="led"></span>
    <span class="value">{mode}</span></span>
  <span class="pill ok"><span class="led"></span><span class="value">CONECTAT</span></span>
  {alarm_pill}
  <span class="spacer"></span>
  <span class="pill"><span class="key">actualizat</span><span class="value">14:32:07</span></span>
</header>"""


def fig_topbar() -> str:
    return ui_page(top_bar(alarm=True, running=True), 820, bg="#0f1319")


# --- the whole application, on Home -------------------------------------------

TABS = [
    ("Home", []),
    ("Control manual", ["Miscare", "Pneumatic", "RFID", "Analogice"]),
    ("Stare sistem", ["Valori", "Animat sus", "Animat fata"]),
    ("Service", ["Diagnostic", "Configuratie", "Conexiune", "Simboluri"]),
]

# label, background, text colour - the ARGB palette from HMI.ShowCurrentObjectColor,
# and the counter and policy are a plausible run rather than all zeros.
COLOURS = [
    ("Fara culoare", "linear-gradient(135deg,#f93,#f0f)", "#12161d", 0, 0),
    ("Rosu", "#FF0000", "#ffffff", 3, 1),
    ("Verde", "#00C000", "#ffffff", 2, 2),
    ("Cyan", "#00ECFF", "#12161d", 1, 1),
    ("Gri", "#A9A9A9", "#12161d", 0, 0),
    ("Portocaliu", "#FFA500", "#12161d", 4, 2),
    ("Alb", "#FFFFFF", "#12161d", 2, 1),
    ("Negru", "#000000", "#ffffff", 0, 2),
]


def nav_menu() -> str:
    """NavMenu draws every tab's sections, then places them all with `order`."""
    items = []
    for index, (label, _) in enumerate(TABS):
        active = " active" if label == "Home" else ""
        items.append(
            f'<a class="nav-item{active}" style="--order:{index * 2}">'
            f'<span class="dot"></span><span class="label">{label}</span></a>'
        )

    for index, (_, sections) in enumerate(TABS):
        if not sections:
            continue
        children = "".join(
            f'<button class="nav-item nav-child">'
            f'<span class="tick"></span><span class="label">{name}</span></button>'
            for name in sections
        )
        items.append(
            f'<div class="nav-children" style="--order:{index * 2 + 1}">{children}</div>'
        )

    return f'<nav class="hmi-nav">{"".join(items)}</nav>'


def home_page() -> str:
    radios = lambda name, chosen, count: "".join(  # noqa: E731
        f'<td class="mid"><input class="pick" type="radio" name="{name}"'
        f'{" checked" if chosen == option else ""}></td>'
        for option in range(count)
    )

    colour_rows = "".join(
        f"<tr><td><span class=\"color-pill\" style=\"background:{fill};"
        f"background-origin:border-box;background-repeat:no-repeat;color:{ink}\">"
        f'{label}</span></td><td class="num">{count}</td>'
        f"{radios(f'policy-{i}', policy, 3)}</tr>"
        for i, (label, fill, ink, count, policy) in enumerate(COLOURS)
    )

    pallet_rows = "".join(
        f'<tr><td>{i + 1}</td>{radios(f"pallet-{i}", i % 2, 2)}</tr>'
        for i in range(6)
    )

    return f"""<main class="hmi-main">
<div class="panel" style="margin-bottom:14px">
  <header><h2>Comanda ciclu</h2><span class="spacer"></span>
    <div class="lamps-row compact">
      <div class="lamp-unit"><div class="lamp-bulb good"></div>
        <div class="lamp-name">Auto</div><div class="lamp-state">in ciclu</div></div>
      <div class="lamp-unit"><div class="lamp-bulb on warn"></div>
        <div class="lamp-name">Manual</div><div class="lamp-state">oprita</div></div>
    </div>
  </header>
  <div class="body"><div class="actions"><div class="commands">
    <button class="cmd start">START</button>
    <button class="cmd pause" disabled>PAUZA</button>
    <button class="cmd endstep" disabled>PAUZA LA FINAL DE&nbsp;PAS</button>
    <button class="cmd reset">RESET</button>
  </div></div></div>
</div>

<div class="panel">
  <header><h2>Sortare</h2></header>
  <div class="body">
    <div class="grid" style="grid-template-columns:repeat(auto-fit,minmax(330px,1fr))">
      <div class="table-fit"><h3>Pe culoare</h3>
        <table class="data tight"><thead><tr>
          <th>Culoare</th><th class="num">Nr.</th><th class="mid">Lasa</th>
          <th class="mid">Stanga</th><th class="mid">Dreapta</th>
        </tr></thead><tbody>{colour_rows}</tbody></table>
      </div>
      <div class="table-fit"><h3>Pe paleta</h3>
        <table class="data tight"><thead><tr>
          <th>Paleta</th><th class="mid">Stanga</th><th class="mid">Dreapta</th>
        </tr></thead><tbody>{pallet_rows}</tbody></table>
      </div>
    </div>
  </div>
</div>
</main>"""


def fig_app_home() -> str:
    """The whole shell, on Home, with the cell stopped - so the buttons that are
    live are the ones that make sense there."""
    body = (f'<div class="hmi">{top_bar(alarm=False, running=False)}'
            f"{nav_menu()}{home_page()}</div>")
    return ui_page(body, None, bg="#10141b")


def fig_cycle() -> str:
    def row(label: str, run: bool) -> str:
        return f"""<div class="figrow">
  <div class="figlabel">{label}</div>
  <div class="commands">
    <button class="cmd start" {"disabled" if run else ""}>START</button>
    <button class="cmd pause" {"" if run else "disabled"}>PAUZA</button>
    <button class="cmd endstep" {"" if run else "disabled"}>PAUZA LA FINAL DE&nbsp;PAS</button>
    <button class="cmd reset" {"disabled" if run else ""}>RESET</button>
  </div>
</div>"""

    extra = """
.figrow{padding:14px 16px;}
.figrow + .figrow{border-top:1px solid var(--line-soft);}
.figlabel{font-family:'Segoe UI',sans-serif;font-size:11px;text-transform:uppercase;
  letter-spacing:.08em;color:var(--text-faint);margin-bottom:9px;}
"""
    return ui_page(row("celula oprita", run=False) + row("celula in ciclu", run=True),
                   700, extra=extra)


def fig_lamps() -> str:
    body = """<div class="lamps-row" style="padding:16px">
  <div class="lamp-unit"><div class="lamp-bulb on good"></div>
    <div class="lamp-name">Retras</div></div>
  <div class="lamp-unit"><div class="lamp-bulb warn"></div>
    <div class="lamp-name">Extins</div></div>
  <div class="lamp-unit"><div class="lamp-bulb on warn"></div>
    <div class="lamp-name">Alimentare</div><div class="lamp-state">brat</div></div>
  <div class="lamp-unit"><div class="lamp-bulb on bad"></div>
    <div class="lamp-name">Eroare</div><div class="lamp-state">brat</div></div>
  <div class="lamp-unit"><div class="lamp-bulb on info"></div>
    <div class="lamp-name">Comanda</div><div class="lamp-state">vacuum</div></div>
</div>"""
    return ui_page(body, 560)


def fig_alarm() -> str:
    body = """<div class="figwrap">
  <div class="figcol">
    <button class="pill bad alarm sounding"><span class="led"></span>
      <span class="value">FARA AER IN SISTEM</span></button>
    <div class="figcap">suna &mdash; becul clipeste</div>
  </div>
  <div class="figcol">
    <button class="pill bad alarm muted"><span class="led"></span>
      <span class="value">FARA AER IN SISTEM</span><span class="key">MUT</span></button>
    <div class="figcap">amutita &mdash; becul sta aprins</div>
  </div>
</div>"""
    extra = """
.figwrap{display:flex;gap:26px;padding:16px;align-items:flex-start;}
.figcol{display:flex;flex-direction:column;gap:8px;align-items:flex-start;}
.figcap{font-family:'Segoe UI',sans-serif;font-size:11px;color:var(--text-faint);}
"""
    return ui_page(body, 620, extra=extra)


def fig_hold() -> str:
    body = """<div class="figwrap">
  <div class="commands"><button class="cmd-hold">INAPOI</button>
    <button class="cmd-hold held">INAINTE</button></div>
</div>"""
    return ui_page(body, 360, extra=".figwrap{padding:16px;}")


# name -> options.
#   size      window, in CSS pixels
#   crop      background colour to trim against; None takes the top-left pixel,
#             False leaves the image alone (the shell already fills the window)
#   resize    cap the final width, in pixels
FIGURES: dict[str, dict] = {
    # Tall enough for the nav column with every tab unfolded, and no taller -
    # the shell fills the window, so spare height would come out as dead space.
    "app-home":      {"build": fig_app_home,  "size": (1280, 700), "crop": False,
                      "resize": 1600},
    "stand-top":     {"build": stand_top,     "size": (780, 940)},
    "stand-front":   {"build": stand_front,   "size": (1180, 800)},
    "topbar":        {"build": fig_topbar,    "size": (820, 140), "crop": "#0f1319"},
    "cycle-buttons": {"build": fig_cycle,     "size": (700, 280)},
    "lamps":         {"build": fig_lamps,     "size": (560, 200)},
    "alarm-pill":    {"build": fig_alarm,     "size": (620, 160)},
    "hold-button":   {"build": fig_hold,      "size": (360, 140)},
}


def find_edge() -> Path:
    for path in EDGE_CANDIDATES:
        if path.exists():
            return path
    raise SystemExit("Microsoft Edge was not found; the figures are rendered with it.")


def shoot(edge: Path, name: str, html: str, size: tuple[int, int]) -> Path:
    target = OUT / f"{name}.png"
    target.unlink(missing_ok=True)
    width, height = size

    # Edge holds the profile a moment after it exits, so removing the temporary
    # folder must not be fatal.
    with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
        src = Path(tmp) / "fig.html"
        src.write_text(html, encoding="utf-8")

        subprocess.run(
            [str(edge), "--headless=new", "--disable-gpu", "--no-sandbox",
             f"--user-data-dir={Path(tmp) / 'prof'}",
             f"--force-device-scale-factor={SCALE}",
             f"--screenshot={target}", f"--window-size={width},{height}",
             src.as_uri()],
            check=True, timeout=180,
        )

        # The process we waited for is not the one writing the file: Edge returns
        # before rendering finishes. If the temporary folder went away now, the
        # page would go with it - so the wait happens here, inside.
        size_seen = -1
        for _ in range(120):
            if target.exists():
                current = target.stat().st_size
                if current > 0 and current == size_seen:
                    break
                size_seen = current
            time.sleep(0.5)
        else:
            raise SystemExit(f"{name}: Edge ran, but the image never appeared.")

    return target


def finish(path: Path, crop, resize: int | None) -> tuple[int, int]:
    """Trim to content and cap the width.

    Without an explicit colour the background is taken from the top-left pixel.
    That is wrong when the figure fills the corner and it is the page around it
    that is empty - the top bar is such a case - so there the colour is given.
    """
    from PIL import Image, ImageChops

    image = Image.open(path).convert("RGB")

    if crop is not False:
        empty = crop if crop else image.getpixel((0, 0))
        box = ImageChops.difference(image, Image.new("RGB", image.size, empty)).getbbox()
        if box:
            pad = 10 * SCALE
            image = image.crop((max(box[0] - pad, 0), max(box[1] - pad, 0),
                                min(box[2] + pad, image.width),
                                min(box[3] + pad, image.height)))

    if resize and image.width > resize:
        height = round(image.height * resize / image.width)
        image = image.resize((resize, height), Image.LANCZOS)

    image.save(path, optimize=True)
    return image.size


def main() -> int:
    edge = find_edge()
    OUT.mkdir(parents=True, exist_ok=True)

    for name, options in FIGURES.items():
        path = shoot(edge, name, options["build"](), options["size"])
        width, height = finish(path, options.get("crop"), options.get("resize"))
        print(f"  {name:16s} {width}x{height}  ({round(path.stat().st_size / 1024)} KB)")

    print(f"\n{len(FIGURES)} images in {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
