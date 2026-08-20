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


def nav_menu(tab: str = "Home", section: str = "") -> str:
    """NavMenu draws every tab's sections, then places them all with `order`. A section
    is marked active only inside the open tab, as in the component."""
    items = []
    for index, (label, _) in enumerate(TABS):
        active = " active" if label == tab else ""
        items.append(
            f'<a class="nav-item{active}" style="--order:{index * 2}">'
            f'<span class="dot"></span><span class="label">{label}</span></a>'
        )

    for index, (label, sections) in enumerate(TABS):
        if not sections:
            continue
        current = " current" if label == tab else ""
        children = "".join(
            f'<button class="nav-item nav-child'
            f'{" active" if label == tab and name == section else ""}">'
            f'<span class="tick"></span><span class="label">{name}</span></button>'
            for name in sections
        )
        items.append(
            f'<div class="nav-children{current}" style="--order:{index * 2 + 1}">{children}</div>'
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


# --- the other three tabs ------------------------------------------------------
#
# The cell in these figures is the one the stand views show: one pallet arrived at
# the front of the belt, two queued in the left column, one in the right, the
# pullers home and the cycle stopped. Same state, three ways of looking at it.

SLOT_FILL = {
    "red": "#FF0000", "green": "#00C000", "cyan": "#00ECFF", "gray": "#A9A9A9",
    "orange": "#FFA500", "white": "#FFFFFF", "black": "#000000",
}

SLOT_NAME = {
    "red": "Rosu", "green": "Verde", "cyan": "Cyan", "gray": "Gri",
    "orange": "Portocaliu", "white": "Alb", "black": "Negru", None: "Gol",
}

# 2 | 3 back row, 1 | 0 front row - so the back row is drawn first, as in SlotGrid.
DRAW_ORDER = (2, 3, 1, 0)

# virtual id, RFID (0 = never read), the four slots by index, where it sits
POOL = [
    (0, 1041, ["green", "red", "white", None], "Banda, in fata"),
    (1, 0, [None, "cyan", None, "orange"], "Stanga, in fata"),
    (2, 1043, [None, None, "black", None], "Stanga, pozitia 1"),
    (3, 0, [None, None, None, None], "Dreapta, in fata"),
]

ROWS = [("STANGA", [1, 2], 6, 2), ("BANDA", [0], 6, 0), ("DREAPTA", [3], 6, 1)]


def slotgrid(slots: list, size: str = "md") -> str:
    cells = ""
    for slot in DRAW_ORDER:
        kind = slots[slot]
        if kind is None:
            cells += '<div class="slot empty"></div>'
        else:
            cells += (f'<div class="slot filled" style="background:{SLOT_FILL[kind]};'
                      'background-origin:border-box;background-repeat:no-repeat"></div>')
    return f'<div class="slotgrid size-{size}">{cells}</div>'


def lamp(name: str, state: str = "", on: bool = False, tone: str = "good") -> str:
    tail = f'<div class="lamp-state">{state}</div>' if state else ""
    return (f'<div class="lamp-unit"><div class="lamp-bulb {"on " if on else ""}{tone}"></div>'
            f'<div class="lamp-name">{name}</div>{tail}</div>')


def hold(label: str, held: bool = False) -> str:
    return f'<button class="cmd-hold{" held" if held else ""}">{label}</button>'


def panel(title: str, body: str, note: str = "") -> str:
    tail = f'<span class="spacer"></span><span class="faint">{note}</span>' if note else ""
    return (f'<div class="panel"><header><h2>{title}</h2>{tail}</header>'
            f'<div class="body">{body}</div></div>')


def pneumatic_page() -> str:
    """Control manual - Pneumatic. The commands are allowed, so no banner shows, and
    one button is drawn held: press-and-hold is the whole point of the page."""
    def group(title, lamps, buttons=()):
        body = f'<div class="lamps-row">{"".join(lamps)}</div>'
        if buttons:
            body += f'<div class="actions"><div class="commands">{"".join(buttons)}</div></div>'
        return panel(title, body)

    groups = [
        group("Brat",
              [lamp("Retras", on=True), lamp("Extins", tone="warn"),
               lamp("Comanda", "extindere", tone="info"),
               lamp("Comanda", "retragere", tone="info")],
              [hold("RETRAGE"), hold("EXTINDE")]),
        group("Gripper",
              [lamp("Inchis", tone="warn"), lamp("Vacuum", "detectat"),
               lamp("Comanda", "gripper", tone="info"),
               lamp("Comanda", "vacuum", tone="info")],
              [hold("INCHIDE"), hold("VACUUM")]),
        group("Magazie",
              [lamp("Sus spate", "retrasa", on=True), lamp("Sus fata", "retrasa", on=True),
               lamp("Jos stanga", "retrasa", on=True), lamp("Jos dreapta", "retrasa", on=True)],
              [hold("RETRAGE SUS"), hold("RETRAGE JOS")]),
        group("Pullere",
              [lamp("Stanga", "retras", on=True), lamp("Dreapta", "retras", on=True),
               lamp("Stanga", "extins", tone="warn"), lamp("Dreapta", "extins", tone="warn")],
              [hold("RETRAGE", held=True), hold("SCOATE")]),
        group("Senzori de prezenta",
              [lamp("Spate", "departe"), lamp("Spate", "aproape", on=True),
               lamp("Fata", "departe"), lamp("Fata", "aproape", on=True)]),
        group("Altele",
              [lamp("Aer comprimat", "lipsa", tone="bad"),
               lamp("Magazie", "goala", tone="warn"),
               lamp("Buton start", "pe stand", tone="info")]),
    ]
    return f'<main class="hmi-main"><div class="grid cols-2">{"".join(groups)}</div></main>'


def state_page() -> str:
    """Stare sistem - Valori: the effectors, the three columns, the pool and the table."""
    def effector(label, inner):
        return (f'<div class="effector"><div class="effector-label">{label}</div>'
                f'{inner}</div>')

    empty = '<div class="effector-empty">gol</div>'
    effectors = ('<div class="effectors">'
                 + effector("GRIPPER", empty)
                 + effector("VACUUM", empty)
                 + effector("SENZOR DE CULOARE", '<div class="effector-empty">nimic</div>')
                 + '</div>')

    cards = ""
    for name, ids, capacity, _ in ROWS:
        # The queue is drawn bottom-up, so the pallet in front sits at the bottom.
        slots = ""
        for index in range(capacity):
            if index < len(ids):
                pallet = POOL[ids[index]]
                slots += f'<div class="queue-slot used">{slotgrid(pallet[2], "sm")}</div>'
            else:
                slots += '<div class="queue-slot free"></div>'
        percent = round(len(ids) * 100 / capacity)
        cards += (
            '<div class="row-card"><header>'
            f'<span class="row-name">{name}</span><span class="spacer"></span></header>'
            '<div class="capacity"><div class="capacity-bar">'
            f'<div class="capacity-fill" style="width:{percent}%"></div></div>'
            f'<span class="capacity-text">{len(ids)} / {capacity}</span></div>'
            f'<div class="queue">{slots}</div></div>'
        )
    columns = f'<div class="grid cols-rows">{cards}</div>'

    pool = ""
    for virtual, real, slots, where in POOL:
        rows = "".join(
            f'<div class="pallet-slot"><span class="faint">{slot}</span>'
            f'<span class="{"faint" if slots[slot] is None else ""}">{SLOT_NAME[slots[slot]]}</span></div>'
            for slot in DRAW_ORDER)
        used = sum(1 for s in slots if s is not None)
        pool += (
            '<div class="pallet-card"><div class="pallet-head">'
            f'<span class="pallet-id">Paleta {virtual}</span>'
            f'<span class="pallet-rfid">{f"RFID {real}" if real else "fara RFID"}</span></div>'
            f'<div class="pallet-body">{slotgrid(slots, "md")}'
            f'<div class="pallet-slots">{rows}</div></div>'
            f'<div class="pallet-foot"><span class="faint">{where}</span>'
            f'<span class="spacer"></span><span class="faint">{used} / 4 ocupate</span></div></div>'
        )
    for free in (4, 5):
        pool += (
            '<div class="pallet-card free"><div class="pallet-head">'
            f'<span class="pallet-id">Paleta {free}</span>'
            '<span class="pallet-rfid">loc liber</span></div>'
            f'<div class="pallet-body">{slotgrid([None] * 4, "md")}'
            '<div class="pallet-slots"></div></div>'
            '<div class="pallet-foot"><span class="faint">Neplasata</span></div></div>'
        )
    cards_panel = f'<div class="grid cards">{pool}</div>'

    head = "".join(f'<th class="num">{"Fata" if i == 0 else f"Poz. {i}"}</th>' for i in range(6))
    body = ""
    for name, ids, capacity, dropped in ROWS:
        cells = "".join(
            f'<td class="num">{ids[i]}</td>' if i < len(ids)
            else '<td class="num"><span class="faint">&mdash;</span></td>'
            for i in range(6))
        pulled = "nu" if name != "BANDA" else "&mdash;"
        body += (f'<tr><td>{name.capitalize()}</td>'
                 f'<td class="num"><b>{len(ids)}</b> / {capacity}</td>{cells}'
                 f'<td class="num faint">{pulled}</td><td class="num">{dropped}</td></tr>')

    table = panel(
        "Coloane",
        '<table class="data"><thead><tr><th>Coloana</th><th class="num">Paleti</th>'
        f'{head}<th class="num">La pullere</th><th class="num">In cos</th></tr></thead>'
        f'<tbody>{body}</tbody></table>',
        note="pozitia 0 e in fata, acolo ajunge bratul")

    return (f'<main class="hmi-main">'
            f'<div class="panel" style="margin-bottom:14px"><div class="body">{effectors}</div></div>'
            f'<div class="panel" style="margin-bottom:14px"><div class="body">{columns}</div></div>'
            f'<div class="panel" style="margin-bottom:14px"><div class="body">{cards_panel}</div></div>'
            f'{table}</main>')


# Real numbers, not invented ones: 421 is what PlcSymbols.All().Distinct() counts,
# 184 is CellLoop + MachineLoop + DiagLoop, and the arrival spread is the one
# measured on the stand at a requested 100 ms.
BINDINGS = [
    ("Main.Run", "ns=2;s=Datalayer.plc.app.Application.sym.Main.Run"),
    ("Main.StartCommand", "ns=2;s=Datalayer.plc.app.Application.sym.Main.StartCommand"),
    ("Main.Layout.Rows[-1]._count", "ns=2;s=...sym.Main.Layout.Rows.-1._count"),
    ("Main.Layout.Rows[-1]._pallets_id[0]", "ns=2;s=...sym.Main.Layout.Rows.-1._pallets_id.0"),
    ("Main.Layout.Pool[0]._objectTypes[3]", "ns=2;s=...sym.Main.Layout.Pool.0._objectTypes.3"),
    ("Main.ArmController.ReadPosition.Position", "ns=2;s=...sym.Main.ArmController.ReadPosition.Position"),
    ("HMI.ArmJogLeft", "ns=2;s=Datalayer.plc.app.Application.sym.HMI.ArmJogLeft"),
    ("HMI.ConveyorDistance", "ns=2;s=Datalayer.plc.app.Application.sym.HMI.ConveyorDistance"),
    ("IOs.Air_Presure_Ok", "ns=2;s=Datalayer.plc.app.Application.sym.IOs.Air_Presure_Ok"),
    ("IOs.Puller_Left_Extended", "ns=2;s=...sym.IOs.Puller_Left_Extended"),
    ("GVL_Diag.Diag.Last.Source", "ns=2;s=...sym.GVL_Diag.Diag.Last.Source"),
    ("GVL_Config.Arm.Pos.TravelMax", "ns=2;s=...sym.GVL_Config.Arm.Pos.TravelMax"),
]

BROWSED = [
    ("Main.Layout.Rows.-1._count", "ns=2;s=...Rows.-1._count", "-1"),
    ("Main.Layout.Rows.-1._pallets_id.0", "ns=2;s=...Rows.-1._pallets_id.0", "-1"),
    ("Main.Layout.Rows.0._count", "ns=2;s=...Rows.0._count", "-1"),
    ("Main.Layout.IsAtFront.-1", "ns=2;s=...IsAtFront.-1", "-1"),
    ("Main.Layout.Pool.0._objectTypes.0", "ns=2;s=...Pool.0._objectTypes.0", "-1"),
]


def symbols_page() -> str:
    """Service - Simboluri: what bound, who is delivering the values, and at what rate."""
    bindings = "".join(
        f'<tr><td class="mono">{name}</td>'
        '<td><span class="pill ok" style="padding:2px 8px"><span class="led"></span>'
        '<span class="value">legat</span></span></td>'
        f'<td class="mono faint">{node}</td></tr>'
        for name, node in BINDINGS)

    browsed = "".join(
        f'<tr><td class="mono">{path}</td><td class="mono faint">{node}</td>'
        f'<td class="num faint">{rank}</td></tr>'
        for path, node, rank in BROWSED)

    return f"""<main class="hmi-main">
<div class="banner ok">
  <strong>421 / 421 simboluri legate</strong>
  <span class="dim">radacina: <span class="mono">Datalayer.plc.app.Application.sym</span></span>
  <span class="dim">755 variabile gasite la browse</span>
</div>

<div class="banner ok" style="margin-top:10px">
  <strong>Serverul trimite 184 noduri, la 100 ms</strong>
  <span class="dim">esantionare 100 ms</span>
  <span class="dim">sosesc din 7 in 336 ms, in medie 135</span>
</div>

<div class="panel" style="margin-top:14px">
  <header><h2>Legari</h2><span class="spacer"></span>
    <div class="check" style="margin:0">
      <input type="checkbox" id="onlyUnbound"><label for="onlyUnbound">doar nelegate</label>
    </div>
  </header>
  <div class="body table-scroll">
    <table class="data">
      <thead><tr><th>Nume logic</th><th>Stare</th><th>NodeId / cale gasita</th></tr></thead>
      <tbody>{bindings}</tbody>
    </table>
    <p class="faint" style="margin-top:10px">Se afiseaza primele 12 din 421. Foloseste filtrul.</p>
  </div>
</div>

<div class="panel" style="margin-top:14px">
  <header><h2>Spatiul de adrese, asa cum a fost gasit</h2>
    <span class="faint">util cand un simbol nu se leaga: aici se vede cum il numeste serverul</span>
  </header>
  <div class="body table-scroll">
    <table class="data">
      <thead><tr><th>Cale</th><th>NodeId</th><th class="num">ValueRank</th></tr></thead>
      <tbody>{browsed}</tbody>
    </table>
  </div>
</div>
</main>"""


def shell(page: str, tab: str, section: str) -> str:
    body = (f'<div class="hmi">{top_bar(alarm=False, running=False)}'
            f"{nav_menu(tab, section)}{page}</div>")
    return ui_page(body, None, bg="#10141b")


def fig_app_manual() -> str:
    return shell(pneumatic_page(), "Control manual", "Pneumatic")


def fig_app_state() -> str:
    return shell(state_page(), "Stare sistem", "Valori")


def fig_app_symbols() -> str:
    return shell(symbols_page(), "Service", "Simboluri")


# name -> options.
#   size      window, in CSS pixels
#   crop      background colour to trim against; None takes the top-left pixel,
#             False leaves the image alone (the shell already fills the window)
#   resize    cap the final width, in pixels

# --- drawn schematics ---------------------------------------------------------
#
# The renders above show *what is on screen*. These two show *how things are
# arranged*, which cannot be photographed. They are written out as SVG, so they
# stay vector in print and still display on GitHub.
#
# The captions and body text are Romanian: they are read by the operator.

DIAGRAMS: dict[str, str] = {}

DIAGRAMS["celula"] = """
<svg viewBox="0 0 560 292" xmlns="http://www.w3.org/2000/svg" role="img"
     font-family="Segoe UI, Calibri, sans-serif">
  <defs>
    <marker id="ar" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6"
            markerHeight="6" orient="auto-start-reverse">
      <path d="M0,0 L10,5 L0,10 z" fill="#8a929e"/>
    </marker>
  </defs>

  <!-- magazia, in spate -->
  <rect x="212" y="10" width="136" height="34" rx="4" fill="#e8ebf0" stroke="#c2c7d0"/>
  <text x="280" y="32" text-anchor="middle" font-size="12" fill="#14181f">Magazie</text>
  <line x1="280" y1="44" x2="280" y2="66" stroke="#8a929e" stroke-width="1.4"
        marker-end="url(#ar)"/>

  <!-- cele trei coloane -->
  <g stroke="#c2c7d0" fill="#f4f6f9">
    <rect x="44"  y="68" width="136" height="170" rx="4"/>
    <rect x="212" y="68" width="136" height="170" rx="4"/>
    <rect x="380" y="68" width="136" height="170" rx="4"/>
  </g>

  <g font-size="11.5" text-anchor="middle" fill="#14181f">
    <text x="112" y="86">Coloana stanga</text>
    <text x="280" y="86">Banda</text>
    <text x="448" y="86">Coloana dreapta</text>
  </g>
  <g font-size="10" text-anchor="middle" fill="#8a929e">
    <text x="112" y="99">-1</text>
    <text x="280" y="99">0</text>
    <text x="448" y="99">1</text>
  </g>

  <!-- paletii: pozitia 0 e cea din fata, jos -->
  <g stroke="#7fa8d8" fill="#cfe0f2">
    <rect x="66" y="196" width="92" height="30" rx="3"/>
    <rect x="66" y="162" width="92" height="30" rx="3"/>
    <rect x="234" y="196" width="92" height="30" rx="3"/>
    <rect x="402" y="196" width="92" height="30" rx="3"/>
  </g>
  <g font-size="10.5" text-anchor="middle" fill="#2c3a4d">
    <text x="112" y="216">pozitia 0</text>
    <text x="112" y="182">pozitia 1</text>
    <text x="280" y="216">pozitia 0</text>
    <text x="448" y="216">pozitia 0</text>
  </g>

  <!-- bratul, in fata -->
  <rect x="44" y="252" width="472" height="30" rx="4" fill="#e8ebf0" stroke="#c2c7d0"/>
  <text x="280" y="271" text-anchor="middle" font-size="12" fill="#14181f">
    Bratul ajunge aici &mdash; in fata fiecarei coloane
  </text>

  <g stroke="#8a929e" stroke-width="1.4" fill="none">
    <line x1="158" y1="238" x2="158" y2="250" marker-end="url(#ar)"/>
    <line x1="280" y1="238" x2="280" y2="250" marker-end="url(#ar)"/>
    <line x1="402" y1="238" x2="402" y2="250" marker-end="url(#ar)"/>
  </g>
</svg>"""

DIAGRAMS["piston"] = """
<svg viewBox="0 0 560 148" xmlns="http://www.w3.org/2000/svg" role="img"
     font-family="Segoe UI, Calibri, sans-serif">
  <g font-size="11" text-anchor="middle" fill="#14181f">
    <text x="93" y="16">retras</text>
    <text x="280" y="16">extins</text>
    <text x="467" y="16">nu se stie unde e</text>
  </g>

  <!-- cate un cilindru pentru fiecare stare -->
  <g stroke="#c2c7d0" fill="#eef0f4">
    <rect x="18"  y="30" width="150" height="30" rx="3"/>
    <rect x="205" y="30" width="150" height="30" rx="3"/>
    <rect x="392" y="30" width="150" height="30" rx="3"/>
  </g>
  <g fill="#8a929e">
    <rect x="22"  y="34" width="40" height="22" rx="2"/>
    <rect x="311" y="34" width="40" height="22" rx="2"/>
    <rect x="457" y="34" width="40" height="22" rx="2"/>
  </g>

  <!-- becurile celor doi senzori de capat -->
  <g font-size="9.5" fill="#4d5663">
    <circle cx="30"  cy="80" r="5" fill="#38b869" stroke="#2b8f52"/>
    <text x="42" y="84" text-anchor="start">senzor retras</text>
    <circle cx="30"  cy="98" r="5" fill="#ffffff" stroke="#c2c7d0"/>
    <text x="42" y="102" text-anchor="start">senzor extins</text>

    <circle cx="217" cy="80" r="5" fill="#ffffff" stroke="#c2c7d0"/>
    <text x="229" y="84" text-anchor="start">senzor retras</text>
    <circle cx="217" cy="98" r="5" fill="#38b869" stroke="#2b8f52"/>
    <text x="229" y="102" text-anchor="start">senzor extins</text>

    <circle cx="404" cy="80" r="5" fill="#ffffff" stroke="#c2c7d0"/>
    <text x="416" y="84" text-anchor="start">senzor retras</text>
    <circle cx="404" cy="98" r="5" fill="#ffffff" stroke="#c2c7d0"/>
    <text x="416" y="102" text-anchor="start">senzor extins</text>
  </g>

  <rect x="392" y="118" width="150" height="22" rx="3" fill="#fdf3f3" stroke="#e3b9bb"/>
  <text x="467" y="133" text-anchor="middle" font-size="9.5" fill="#8c2f34">
    ori merge, ori s-a blocat
  </text>
</svg>"""


def write_diagrams() -> None:
    for name, svg in DIAGRAMS.items():
        path = OUT / f"diagram-{name}.svg"
        path.write_text(svg.strip() + chr(10), encoding="utf-8")
        print(f"  diagram-{name:8s} {round(path.stat().st_size / 1024)} KB (SVG)")



FIGURES: dict[str, dict] = {
    # Tall enough for the nav column with every tab unfolded, and no taller -
    # the shell fills the window, so spare height would come out as dead space.
    "app-home":      {"build": fig_app_home,  "size": (1280, 700), "crop": False,
                      "resize": 1600},
    "app-manual":    {"build": fig_app_manual, "size": (1020, 1000), "crop": False,
                      "resize": 1600},
    "app-state":     {"build": fig_app_state,  "size": (1020, 1278), "crop": False,
                      "resize": 1600},
    "app-symbols":   {"build": fig_app_symbols, "size": (1020, 1078), "crop": False,
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

    write_diagrams()

    print(f"\n{len(FIGURES) + len(DIAGRAMS)} images in {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
