# User manual — HMI ZEM System

For whoever operates the stand. The manual is organised around what you have to do, not around an
inventory of the screen: each section is one job taken from start to finish.

You need nothing from the application code to read it. If you are taking over development, the
starting point is `HMI-HANDOFF.md` in the repository root, not this document.

> **A note on language.** The interface itself is in Romanian, because the stand is operated in
> Romanian. Every on-screen label quoted here is therefore given in Romanian, exactly as it appears
> on the tablet. A Romanian edition of this manual is handed to operators as a PDF.

---

## 1. What the application does

It replaces the visualization that used to run on the stand's panel. It connects to the ctrlX CORE
controller, shows the state of the cell, commands the automatic cycle and allows manual movements.

It runs on an **Android tablet** — that is the target — and on **Windows**, where it is used mostly
for checks. The interface is the same on both.

The application also starts **without the stand**, on an internal simulator with four pallets
already in place. That is useful for getting familiar with the screens without moving anything real
(section 3).

---

## 2. What is always visible: the top bar

The top bar is the one thing that never disappears, whichever page you are on. The rest of the
interface shows its message banners **only when they have something to say** — if you see no
coloured banner, that is a good sign, not a sign that something failed to load.

| what you see | what it means |
|---|---|
| `AUTO` | the cell is running the automatic cycle |
| `MANUAL` | the cell is not running; manual commands are allowed |
| `SE RESETEAZA` | a reset is under way; no command is accepted until it finishes |
| `CONECTAT` | the link to the PLC is alive and values are arriving |
| `CONECTARE` | the link is being attempted |
| `EROARE` | the link has dropped — this also sounds as an alarm |
| `DECONECTAT` | not connected and not trying (automatic reconnection is off) |
| `SIMULATOR` | **you are not on the real stand**; nothing you press moves anything |
| `actualizat HH:mm:ss` | the time of the last value received |

The `actualizat` clock is the quickest check that the picture is alive. If it stands still, values
have stopped arriving, even if the bar still reads `CONECTAT`.

Alarms appear here too, as red pills. See section 12.

---

## 3. Starting up and connecting

At startup the application tries to reach the controller by itself if automatic reconnection is
ticked. There is nothing to press — you watch the link pill in the top bar.

To change where it connects, or to work without the stand, go to **Service → Conexiune**:

- **Foloseste simulatorul intern (fara PLC)** — tick it and the cell becomes simulated. The top bar
  shows `SIMULATOR` for as long as it is active.
- **Endpoint OPC UA** — the controller's address.
- **Endpoint securizat**, **Utilizator**, **Parola**, anonymous login — as the server is configured.
  The user fields grey out if you tick anonymous login.
- **Interval de refresh (ms)** — how often you ask for new values. Read section 16 as well, for what
  that number actually means.
- **Reconectare automata** — whether the application connects by itself, and retries after a drop.

Changes take effect on **Salveaza si reconecteaza**. **Renunta la modificari** puts the fields back
to what was saved.

On the right, the **Starea legaturii** panel gives you the state, the source (address or simulator),
how many symbols bound out of how many, and the last error.

---

## 4. How the interface is laid out

Four tabs: **Home**, **Control manual**, **Stare sistem**, **Service**. Each one except Home has
sub-pages of its own.

On a wide screen the menu is a column on the left with every tab unfolded, so you can see what each
one holds. On a narrow screen (tablet held upright) it becomes a band, which shows the sub-pages of
the open tab only. They are the same buttons, laid out differently.

Each tab remembers the sub-page you left it on, so moving to another tab and back does not send you
to the beginning.

In the body of each panel, **the command is at the top and the state below it**. Two places are
deliberate exceptions — **Pneumatic** and **Miscare → Alimentare** — where the lamps sit above the
buttons, because there the state is not the result of pressing but the thing you read **before** you
press.

---

## 5. Starting and stopping the automatic cycle

Everything is on **Home**, in the **Comanda ciclu** panel. Buttons that make no sense at that moment
are greyed out — a greyed-out button is not broken, it is a command the PLC would ignore anyway.

| step | what you press | when it is available |
|---|---|---|
| start the cycle | **START** | the cell is stopped and not resetting |
| stop at the end of the current operation | **PAUZA** | the cell is running |
| stop at the end of the program step | **PAUZA LA FINAL DE PAS** | the cell is running |
| bring the cell back to its start position | **RESET** | the cell is stopped and not resetting |

The **Auto** and **Manual** lamps next to the buttons say the same thing as the mode pill in the top
bar — they are there only so you do not have to move your eyes while pressing.

**One thing to know:** after `PAUZA` the screen looks exactly as it does after an ordinary stop. The
controller does not publish a separate paused state, so the HMI has no way of telling them apart.
See section 16.

---

## 6. Changing the sorting rules

Also on **Home**, the **Sortare** panel. Two tables, two independent decisions.

**Pe culoare** — for each colour you pick one of three: **Lasa**, **Stanga**, **Dreapta**. The
**Nr.** column shows how many objects of that colour have been counted. What the columns mean is
written once, in the table head.

**Pe paleta** — for each pallet you pick **Stanga** or **Dreapta**.

The choice goes to the PLC the moment you make it; there is no confirm button. If the cell is
running in automatic, the radio buttons are greyed out.

---

## 7. Moving the arm and the belt by hand

**Control manual → Miscare.** If the **COMENZI BLOCATE** banner appears at the top, the reason is
written next to it: the system is running in automatic, the system is resetting, or there is no
connection. Stop the cycle first.

**Power first, before anything else.** The **Alimentare** panel gives you, separately for
**Conveior** and for **Brat**: the **Alimentare** lamp, the **Eroare** lamp, and three buttons —
**ALIMENTEAZA**, **STOP**, **RESET**. An axis in error will not move until you reset it.

**Jog** — four buttons: `INAPOI` / `INAINTE` for the conveyor, `STANGA` / `DREAPTA` for the arm.

> **The jog buttons are press-and-hold.** The axis moves only while your finger is on the button and
> stops the moment you lift it. Read section 13 before using them for the first time.

**Moving to a position** — type the position into the **Pozitie absoluta** field and press **MERGI
LA**. While the arm travels, the button reads `SE DEPLASEAZA` and will not take a second command.

**Relative movement** — the same, but relative to the current position, with the **DEPLASEAZA**
button. If the **In afara cursei** lamp is lit, a relative move would take the arm out of its
travel and the button stays greyed out; bring it back into travel first, with the jog or with an
absolute position.

The **Altele** panel shows the pallet distance, the arm position and the travel limits. The
**Viteze** panel shows the speeds configured in the stand. Both are **read-only** — they are changed
in the PLC configuration, not from here.

---

## 8. Commanding the pneumatics directly

**Control manual → Pneumatic.** The buttons here write straight to the outputs, **over the sequence
logic**. They work only while the system is stopped, and only while you hold them down.

In each panel the lamps are above the buttons: you read the state, then you press.

| panel | lamps | buttons |
|---|---|---|
| **Brat** | retracted, extended, extend command, retract command | `RETRAGE`, `EXTINDE` |
| **Gripper** | closed, vacuum detected, gripper command, vacuum command | `INCHIDE`, `VACUUM` |
| **Magazie** | the four gates retracted (upper back, upper front, lower left, lower right) | `RETRAGE SUS`, `RETRAGE JOS` |
| **Pullere** | left/right retracted, left/right extended | `RETRAGE`, `SCOATE` |
| **Senzori de prezenta** | back far/near, front far/near | — |
| **Altele** | no compressed air, storage empty, the start button on the stand | — |

The last two panels are read-only.

The **command** lamps show what the PLC is asking for, and the position lamps show what the sensors
answer. When a command is lit and the position does not change, that is where the problem is.

---

## 9. Reading and writing an RFID tag

**Control manual → RFID.** Works only while the system is stopped.

**Check the reader first.** The **Cititor** panel has lamps for: pallet present, tag detected, ready,
antenna on, error, alarm 1, alarm 2. Below them, the **signal strength** bar and the level in
figures. With no tag in front of the antenna, the bytes underneath mean nothing.

**To read** — press **CITESTE TOT**. The eight bytes appear in the left-hand column. A short message
under the tables confirms that the request went out.

**To write:**

1. **PREIA DE LA TAG** copies what was read on the left into the right, so you only change what you
   want.
2. Change the bytes that matter to you; each one accepts 0...255.
3. **SCRIE TOT** sends all eight bytes. While it runs, the button reads `SE SCRIE`.

If one byte cannot be sent, writing stops there and the message tells you which byte — the tag may
be half written, so run the operation again.

---

## 10. Writing the analog outputs

**Control manual → Analogice.** Two panels, **Iesirea 1** and **Iesirea 2**.

You type the value in **mV** and press **SCRIE**. Under the field you see two rows: **Valoarea din
PLC**, what the controller currently holds, and **Cuvant brut pe iesire**, the value actually sent to
the module.

The fields start from what is already in the PLC, so pressing `SCRIE` without editing changes
nothing. As everywhere on Control manual, it works only with the system stopped.

---

## 11. Following the cell

**Stare sistem**, three ways of reading the same data.

**Valori** — in figures:

- **the effectors**: what the gripper holds (with the pallet's slots drawn), what the vacuum holds,
  and what the colour sensor sees;
- one card per column and one per pallet in the system;
- the **Coloane** table: how many pallets out of how many fit, which pallet is on each position,
  whether the column is at the pullers, and how many objects were dropped into the side bin.
  Position 0 is at the front, where the arm reaches.

**Animat sus** and **Animat fata** — the stand drawn from two angles, following the values live.

The drawings are stacks of overlaid images: each part is a whole image that moves across the screen.
Nothing animates internally. In practice that means **a part is drawn in the position its sensors
report**, not somewhere in between — and where the sensors cannot say "I am on my way", the drawing
shows it as arrived. See section 16 for the concrete case of the pneumatic head.

---

## 12. The audible alarm

When something is worth hearing, a **red pill** with a blinking led appears in the top bar and the
application sounds a repeated beat.

It sounds on:

| alarm | when |
|---|---|
| `LEGATURA PIERDUTA` | the link to the PLC dropped during work |
| `OPRIRE — vezi diagnosticul` | the PLC halted and the halt was not acknowledged |
| `FARA AER IN SISTEM` | the air pressure failed |
| `EROARE LA AXA BRATULUI` | the arm axis is in error |
| `EROARE LA AXA BENZII` | the belt axis is in error |

Two things to know:

**The alarm pill is its own mute button.** Press it and the sound stops; press it again and it comes
back. Silenced, the pill **stays on the bar**, with the led lit steady instead of blinking and
labelled `MUT` — the sound went quiet, not the fault.

Silencing is per alarm, so a second fault on top of a silenced one **is heard**. And an alarm that
goes away is forgotten: if it returns, it sounds again.

**The sound only starts after you touch the screen.** That is a browser rule the application cannot
get around. If the tablet has sat untouched since startup, the first alarm shows but is not heard,
and finds its voice at the first touch. If you leave the stand unattended, touch the screen once
after starting the application.

A link you closed on purpose (`DECONECTAT`) does not sound — the link alarm is for a link that was
*lost*, not for one you did not ask for.

---

## 13. Rules of operation

**The controller does not watch over the tablet.** There is no mechanism in the PLC that stops a
movement if the application freezes or the tablet dies. Because of that:

- **the jog and the solenoid valves are press-and-hold.** There is no start with one button and stop
  with another. Lift your finger and it stops.
- the application drops every command by itself when you **leave the page**, when you **leave the
  application**, when you **switch off or lock the screen**, when the window **loses focus**, and
  before any disconnection.

What that means in practice: if you are in the middle of a jog and your phone rings over the
application, the movement stops. That is deliberate.

The rest of the rules, also from the PLC:

- manual commands are blocked while the system is running in automatic or resetting;
- the pneumatics and RFID writing work **only** with the system stopped;
- the speeds, travel limits and stand configuration are read-only from the HMI.

---

## 14. When something has halted: the diagnostics

When the PLC halts with an unacknowledged cause, a red banner appears on **Home** with a button that
takes you straight to the diagnostics. The same halt also sounds as an alarm.

**Service → Diagnostic** gives you:

- **Ultima oprire** — the source (which block of the program), the step, the cause in plain words,
  and the cycle it happened in;
- **Contoare** — how many halts are recorded and the current cycle of the main task;
- **Istoricul opririlor** — the list of previous ones. The **Reciteste** button fetches it from the
  PLC again; the history does not refresh by itself.

The source and the step are what you should pass on when you ask for help — they are enough to find
the place in the PLC program.

**Service → Configuratie** shows, read-only, the configuration the stand works with: the arm's and
the belt's limits and speeds, the timings, and the slot order.

---

## 15. When something does not work

| what you see | what it means | what to do |
|---|---|---|
| the `actualizat` clock stands still although it reads `CONECTAT` | values are no longer arriving | **Service → Simboluri**: it tells you whether the server is still sending anything, and at what interval |
| `EROARE` on the link pill | the link dropped | check the network and the controller; with automatic reconnection ticked, the application retries by itself |
| `DECONECTAT` and nothing happens | automatic reconnection is off | **Service → Conexiune**, tick reconnection or press **Salveaza si reconecteaza** |
| the `COMENZI BLOCATE` banner | the system is running, resetting, or not connected | the banner itself says which of the three; stop the cycle if you want manual commands |
| greyed-out buttons on Home | the command makes no sense right now | see the table in section 5 |
| `SIMULATOR` in the bar | you are not on the real stand | **Service → Conexiune**, clear the simulator tick |
| values that look plausible but never change | possibly an unbound symbol | **Service → Simboluri**, tick **doar nelegate**; an unbound symbol looks in the interface exactly like a zero coming from the PLC |
| the alarm shows but is not heard | the screen has not been touched since startup | touch the screen once (section 12) |
| an axis does not move on jog | the axis is in error or unpowered | **Miscare → Alimentare**: the **Eroare** lamp, then `RESET`, then `ALIMENTEAZA` |
| relative movement is greyed out | the **In afara cursei** lamp is lit | bring the arm back into travel with the jog or with an absolute position |

**Service → Simboluri** is the page to look at when you suspect the picture is lying. It tells you
how many symbols bound out of how many, whether values are pushed by the server or polled by the
application, how many nodes the server is sending and at what real interval.

---

## 16. Known limitations

Things that are not defects to be fixed on the spot, but limits you are better off knowing.

**"Paused" and "stopped" look identical.** The controller publishes whether it is running or not,
but not that it is paused. After `PAUZA` the screen looks exactly as it does after an ordinary stop.
For the same reason, `RESET` is available whenever the cell is not running, not only after a pause.

**A pneumatic head stuck on its way is drawn as raised.** In the animated views the head is drawn
lowered only when the "extended" sensor is made **and** the "retracted" one is not. Any other
combination — including the head stuck between the two positions, with both sensors off — shows it
raised. The pullers have already been moved to a rule that draws the intermediate state too, at half
travel; the head has not. **The truth is in the lamps on Control manual → Pneumatic**, not in the
drawing.

**Values do not arrive at equal intervals.** The application does not fetch values one by one: the
server pushes them when they change, and the interval you ask for in **Service → Conexiune** is a
request, not a guarantee. Cyclic reading has been kept only as a safety net, for the case where
pushing does not work.

Measured on this stand, at 100 ms requested, values arrived between 7 and 336 ms apart, averaging
135 ms. This is not a network problem on the tablet and it is not solved by raising or lowering the
interval at random.

The **Service → Simboluri** page now also shows the **sampling interval the server granted** — how
often the server looks at the value. If that number is larger than the requested interval, that is
the explanation: values arrive on time, but they are already stale when they leave. That is where
the search continues. **The investigation is not closed.**

---

## 17. If you are taking over development

This manual stops at what is visible on screen. What lies underneath is in:

| document | what it holds |
|---|---|
| `HMI-HANDOFF.md` (root) | the application structure, the styling decisions, the safety rules, the traps already paid for, and the dimensions taken from the stand drawings |
| `docs/OPCUA-HMI.md` | the variables the PLC publishes, their types, and what the HMI is allowed to write |
| `docs/HMI-CONTEXT.md` | what each page of the old HMI displayed and what PLC logic sits behind each button |
