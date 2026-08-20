# HMI ZEM System — state of the work

A .NET MAUI Blazor Hybrid application that replaces the CODESYS visualization on the target, over
OPC UA. It runs on an Android tablet (the primary target) and on Windows.

> **A note on language.** Documentation and code comments are in English. The interface itself is in
> Romanian, because the stand is operated in Romanian — so every on-screen label quoted below is
> given in Romanian, exactly as it appears. Operators get `docs/USER-MANUAL.md`, whose Romanian
> edition is handed out as a PDF: its source and the script that builds it live in `manual-ro/`,
> next to the project but gitignored, so that everything inside the repository stays English. When
> one of the two manuals changes, the other one changes with it.

## Read this before anything else

| file | what it holds |
|---|---|
| `docs/OPCUA-HMI.md` | the 755 published variables, types, enums, what the HMI is allowed to write |
| `docs/HMI-CONTEXT.md` | what each page of the old HMI displayed and what logic sits behind it |
| `docs/USER-MANUAL.md` | the operator's manual: workflows, alarms, known limitations |

The first two are enough. The CODESYS project does not need to be opened, and its own handover
document is not carried here — it describes that project's tooling and working tree, not this one.

## How to work with the user

- **Ask before writing code.** They asked for this explicitly, so the work is not done twice.
- Communication in Romanian, no diacritics in the interface.
- Risky actions that the documentation does not settle are raised for discussion, not decided alone.
- Do not write in the interface what is visible or implied. No explanatory prose, no PLC variable
  names on operator pages (those belong on Service and in the technical tables).

## Structure

```
docs/                      OPCUA-HMI, HMI-CONTEXT, USER-MANUAL
ZEM_BoschRexrothSystemByASTI/
  Plc/                     the communication layer and the model
    PlcSymbols.cs          the logical names of every variable + the read groups
    SymbolTable.cs         ties the logical names to NodeIds found by browsing
    OpcUaPlcClient.cs      session, browse, subscription, batch reads, typed writes
    SimulatedPlcClient.cs  a simulated cell, so the HMI runs without a PLC
    PlcService.cs          the refresh loop, commands, dropping the flags
    UiState.cs             the tabs, each one's sections, and which section each has open
    CellSnapshot.cs / MachineSnapshot.cs / HmiSettings.cs
  Components/
    Layout/                MainLayout (top bar), NavMenu (tree)
    Pages/                 Home, Manual, SystemView (/stare), ServiceView (/service), NotFound
    Manual/                MotionPanel, PneumaticPanel, RfidPanel, AnalogPanel
    Service/               DiagnosticsPanel, ConfigurationPanel, SettingsPanel, SymbolsPanel
    Overview/              CellDataView
    Cell/                  StandGeometry, TopView, FrontView (the stand drawings)
    Ui/                    Lamp, Gauge, HoldButton, SlotGrid, PalletCard, RowCard,
                           StatTile, BoolLed
  wwwroot/app.css          the whole theme
  wwwroot/hmi.js           drops the flags on blur / hide / close
  wwwroot/cell/top/        58 drawings, the view from above
  wwwroot/cell/front/      42 drawings, the view from the front
attic/                     pages taken out of the build, kept on disk only - gitignored, along
                           with PLC/, because old unused material stays out of the repository
```

## The four tabs

| tab | contents |
|---|---|
| **Home** | Cycle command (Start, Pauza, Pauza la final de pas, Reset + AUTO/MANUAL lamps), Sortare (by colour with a counter and Lasa/Stanga/Dreapta radios, by pallet with Stanga/Dreapta radios) |
| **Control manual** | sub-tabs Miscare · Pneumatic · RFID · Analogice; at the top, a COMENZI PERMISE / BLOCATE indicator |
| **Stare sistem** | sub-tabs Valori · Animat sus · Animat fata. Valori = effectors (gripper, vacuum, colour sensor), columns, pallets, analog inputs and outputs as gauges. The two animated views are the stand drawings |
| **Service** | sub-tabs Diagnostic · Configuratie · Conexiune · Simboluri |

## Style

A dark theme, following the patterns of the old HMI. Its screenshots are not in the repository — the
patterns worth keeping are written down below.

Two layout conventions, kept everywhere:

- **command on top, state below.** In the body of any panel the `.actions` band comes first; the
  lamps, tables and gauges after it. The field that gets sent is part of the command, so it goes
  into the band too: a single field keeps its button right underneath it, and a command that acts on
  a whole block (the RFID bytes) sits above the block. State that fits on one line moves up into the
  panel header (`.lamps-row.compact` after a `.spacer`), as on Home and on Miscare.
  **The exception, asked for by the user: Pneumatic and Miscare · Alimentare.** There the lamp is not
  the result of the command but the state read before pressing — the axis power and error are seen
  first, the press comes after. The band sits at the foot of the group and the separating line runs
  above it (`.actions:not(:first-child)`).
- **navigation comes from a single place.** The tabs and each one's sections are static tables in
  `UiState`; `NavMenu` renders them all, the pages declare nothing and only ask
  `Ui.SelectedOf("manual")`. Each tab keeps its own section, so moving from one tab to another does
  not reset them, and a section of another tab is a navigation command as well.
  In the **column** every tab is unfolded at once, so that what each one holds is visible; in the
  **band** (portrait) only the open tab's sections show, on a row of their own, from the `current`
  class. A tab and a sub-page are the same button, `.nav-item`; the sub-page is smaller and has a
  dash instead of a dot.

- **a message banner appears only when it has something to say.** No permanent green banners
  confirming the normal: "comenzi blocate" shows only when they are blocked, "oprire activa" only
  when there is one. The absence of a banner is the good news. What stays visible at all times are
  the states in the top bar.

The rest, after the patterns of the old HMI:

- panel titles **bold, italic, capitals**, letter-spaced;
- round lamps with a rim, the caption on two rows underneath (name, then state);
- dark pill buttons; only the cycle commands carry colour;
- 90-degree needle gauges for the analog values;
- empty radios in a table, with the meaning written once in the table head;
- a segmented bar for the RFID signal.

Object colours are the ARGB palette from `HMI.ShowCurrentObjectColor`, so that they look the same as
in the old interface.

## The audible alarm

The list is in `Plc/Alarms.cs` and it is the whole decision; the rest is plumbing. It sounds on: a
lost link (`Faulted`, **not** `Offline` — the HMI sits disconnected on purpose when auto-connect is
off, and an alarm at every startup is one nobody listens to any more), an unacknowledged PLC halt
(`Diag.Active`), no air (`!Air_Presure_Ok`), and an error on either of the two axes.

No air is **no longer** conditioned on `Run`, although it was at first: the PLC drops to manual by
itself when the pressure fails, so the condition would have gone out at the very moment the problem
appeared.

Everything that is not the link is evaluated **only while the connection is alive**. Otherwise
`Air_Presure_Ok` would stay frozen at its last value and the HMI would go on asserting something it
can no longer see.

The sound is drawn from an oscillator in `hmi.js`, with no audio file, so it sounds the same on
Android and on Windows and cannot be lost when packaging. Two things to know:

- **No page is allowed to sound before someone has touched it.** That is the browser rule, and the
  WebView on the tablet keeps it. `hmiAlarm.listen` opens the sound up at the first touch or key
  press; until then the 1.5 s beat runs for nothing, and finds its voice at the first touch.
- **Silencing is per alarm, and the alarm is its own button**: press the pill and the sound starts or
  goes quiet. Silenced, it stays on the bar — the sound went quiet, not the fault — with the led lit
  steady instead of blinking and labelled "MUT". An alarm that goes away is forgotten rather than
  kept silenced, so if it comes back it is heard again.

## Safety rules that are not up for negotiation

**There is no watchdog in the PLC.** A level flag left up means an axis running into its travel
limit. Because of that:

- the jog and the solenoid valves are **press-and-hold** (`HoldButton`), with `FALSE` on release;
- `PlcService.ReleaseAllFlagsAsync()` is called when the page is left, on blur / hide / close through
  `hmi.js`, and before any disconnection. It drops `PlcSymbols.CommandFlags` **and**
  `PlcSymbols.ValveCommands` — a valve is a command too, and a gripper left closed holds a pallet
  nobody is tracking any more;
- lowering a flag is never refused (`PlcService.SetFlagAsync`): raising one needs
  `CanCommandManually`, dropping one has to work even if the cell started running in the meantime;
- the flags that re-arm (`ArmMoveAbsolute`, `ArmMoveRelative`, `RfidRead`, `RfidWrite`,
  `WriteAO1/2`, `*SetPower`) are sent as a **pulse**, not as a level;
- manual commands are blocked while `Main.Run` or `Main.ResetStarted` is true — the PLC does the
  same, so a live button would look broken;
- the pneumatics and RFID writing work only while the system is stopped (the user's decision);
- never write to `Main.Layout`, to the controllers, or to the travel limits.

## Traps already paid for — not to be repeated

1. **Order in CSS at equal specificity — this bit twice.** The portrait rule for `.nav-children` was
   written **before** the base definition, so the base won and the sub-pages stayed in the column, at
   a low `order`, between the tabs. That is why the base definition now sits at the top, next to
   `.nav-item`, and the portrait media query comes after it. The same with `.nav-item:hover` and
   `.nav-item.active`, which weigh as much as `.nav-item.nav-child`: the sub-page rules are written
   with all the classes (`.nav-item.nav-child.active`), or they steal their colour.
2. **`minmax(0, 1fr)`, not `1fr`**, for the shell's content row. The default minimum of a grid track
   is `min-content`, so the page grows past the screen and nothing scrolls any more.
   On a narrow screen (under 820px) the shell becomes `position: static` and scrolls the whole
   document — with no nested scroller, which is what gives way in the Android WebView.
3. **A component with no parameters does not re-render when its parent does.** Blazor skips the child
   if the parameters are "certainly equal", and zero parameters means exactly that. The four panels
   in Control manual were written `<MotionPanel/>`, without parameters, and were not subscribed to
   `Plc.Updated` either — so they redrew **only** when their own buttons raised an event. The power
   led came on when you lifted your finger off the jog and never went out again. The animated pages
   escaped only because they receive `Cell` and `Machine` as parameters: being mutable objects,
   Blazor cannot declare them unchanged.

   The rule: **any component showing values from the PLC inherits `PlcComponentBase`.** Not
   `@inject PlcService` — that gives you the service, but not when something changed. The symptom is
   deceptive: it looks like network latency, and sends you hunting in OPC UA.

4. **Disposal order in Blazor.** The old component is disposed **after** the new one has been
   initialised — so no page may clear on exit state that the next one uses. For sections the problem
   has gone (they are static tables in `UiState`, nobody declares them and nobody clears them), but
   the rule stands for anything else held in services.
5. **`th.num` did not inherit the alignment** of `td.num` — header on the left, values on the right.
6. **A `Components.System` namespace** hides `System` and nothing compiles any more. The folder is
   called `Overview`.
7. **`<text>` in Razor** is a reserved element; inside a code block, SVG needs a `<g>` around it. And
   every SVG coordinate is formatted with `InvariantCulture`.
8. **`@key` has to be unique among siblings.** The pallet in the gripper is drawn twice in the front
   view — the back row under the claws, the front row over them — and the two halves are siblings in
   the same `<div class="stand">`. With the same key, Blazor throws the moment the arm lifts a
   pallet, that is after a minute of running, not when the page opens.
   Also: a literal with quotes does not fit in a Razor attribute (`@key="Key("x", id)"` does not
   compile) — the key is computed in a property.

## The PLC is a ctrlX CORE, not a classic CODESYS

The documentation in `docs/` describes the symbols as they look in the project. The OPC UA server on
the stand publishes them differently, and two things cost an evening:

1. **The application lives in the Data Layer**, at `Datalayer.plc.app.Application.sym`, and the path
   to it passes through nodes published as **variables**, not as objects. The root search now scores
   every node, by how many of the five roots appear among its children, and picks the maximum —
   stopping at the first match catches a node in the diagnostics area that happens to have the same
   name, and then **no** symbol binds at all. Zero bindings, rather than one missing, is the sign of
   the wrong root.
2. **Array elements are child nodes named with the index**, joined with a dot, and the index stays
   the one declared in the PLC: `Main.Layout.Rows.-1._pallets_id.3`, with a literal minus one, not
   rebased to zero. `SymbolTable.SegmentVariants` tries both spellings, with brackets and with a dot,
   and both indices.

The **Service → Simboluri** page is the one that shows all this: the root that was found, how many
symbols bound out of how many, and the address space as browse saw it. Without it, an unbound symbol
looks in the interface exactly like a zero coming from the PLC.

On this stand **all** of them bind. `Main.ConveyorController.ReadPosition.Position` was taken out of
the list: the belt has no axis position, its distance comes from sensors through
`GetCenterDistance()`, and it was not displayed anywhere. While it stayed in the list, the page
permanently showed "1 nelegate" — and a warning that is always on says nothing when something really
is missing. Position is now read for the arm only (`hasPosition` in `ReadAxis`).

The client certificate has to be moved into **Trusted** from `Settings → Certificates & Keys →
OPC UA Server` in the ctrlX web interface. Until then the connection fails with
`BadSecurityChecksFailed`, and the error comes from the server — our client accepts anything.

## How the values reach the HMI

**The server pushes them, the HMI no longer asks for them.** The loop groups (`CellLoop` 78,
`MachineLoop` 98 and `DiagLoop` 8 — 184 nodes) are monitored items in a single subscription; the
client keeps the last value of each node, and a "read" has become a dictionary lookup. Decoding was left
untouched: it still receives a `PlcValues`. Before, there were three reads per cycle, each a round
trip over WiFi, and it was exactly the scatter of that wait that showed up as a picture that sits
still and then jumps.

Reading has been kept as a safety net and is used whenever it is not certain that pushing works: a
server that refuses the subscription, nodes it did not accept, a subscription that has stopped
publishing. **A frozen picture has to become a slow picture, never a wrong one** — otherwise the HMI
shows "CONECTAT" over a cell that has moved in the meantime.

**The loop waits for the publish, not for a clock of its own.** `PlcService.WaitForWorkAsync` blocks
on `OpcUaPlcClient.WaitForPublishAsync` while the subscription is live, and on a plain delay when it
is not. Two clocks that are not the same clock drift against each other, and the drift lands in the
picture: one pass takes a value the moment it arrived, the next takes one that has been sitting
there, so the steps come out uneven however fast the values are. The timeout is four publishing
intervals and it is only a way out — when publishing stops the wait ends, the next cycle finds the
subscription no longer live, and everything goes back to reading. `Plc.UpdatePeriodMs` is read from
the same place, which is what makes a drawing's step exactly as long as the wait for the next value.

The heavy groups (`DiagHistory`, `ColorNames`, `Config`, `Policies`) stay plain reads: they are asked
for once or on demand, and monitoring them would cost the server for nothing.

Two things to keep in mind about threads:

- the publish callback comes in with the subscription lock held, so **nothing from the subscription
  API is called while holding `_liveGate`** — otherwise the two locks meet in reverse order;
- the `ClientHandle → NodeId` map is added to, not rebuilt. Emptied and refilled, it would lose the
  notifications that arrived in between, and a value that changes only once would be lost for good.

The server settings, from `Setup Server` in the ctrlX interface (the limits everything has to fit
inside): publishing and sampling between 10 ms and 10 s, subscription lifetime between 1 s and 100 s,
**500 monitored items per subscription** and 5000 in total, 12 parallel sessions. `Maximum supported
number of values` is 100 — if more nodes than that change at once, the rest arrive at the next
publish, so nothing is lost, it is only late.

The **Service → Simboluri** page says which of the two paths is in use, how many nodes the server is
sending, at what interval, and the sampling interval it granted. `OnDataChange` also keeps the last
32 gaps between publishes, so the page shows the measured spread — smallest, largest, average. That
is where the 7…336 ms at a requested 100 ms comes from; it is one session's observation, not a
measurement with a method behind it.

## The stand drawings

Two views, each a stack of overlaid drawings, all covering the same canvas: 750 × 900 mm from above,
750 × 500 mm from the front. Nothing animates internally — only the whole layer moves, exactly what
the old visualization did. That is why they are files in `wwwroot/cell/`, loaded with `<img>`, and
not inline SVG: the ~100 files share the same class names (`.cls-1`) and the same gradient ids, so
inline they would trample each other.

Movements are written in **percentages** of the box, not in pixels, so there is no px/mm conversion,
no resize listener and no animation loop — the box keeps its proportion from `aspect-ratio`, and the
browser does the transition. The scale bus from the old visualization (`custom.tools`) has no
counterpart any more.

Three things keep the movement continuous, and all three matter:

- **the step is as long as the value period.** `--step` is set on `.stand` from `Plc.UpdatePeriodMs`,
  that is the publishing interval when the server pushes, or the polling interval when it does not. A
  duration picked by eye would be either too short (it sits and waits) or too long (it falls behind).
- **the curve is `linear`.** With `ease-out`, every step braked to a complete stop, so a continuous
  movement of the arm looked like a series of jerks.
- **`will-change: transform` is set by `StandGeometry.Move`**, not by the stylesheet, because that is
  where it is known exactly which layers move. Put on `.layer` in CSS, it would promote to the GPU
  the ~45 drawings that stand still as well, and the tablet's video memory goes for nothing.

All the numbers live in `StandGeometry`. The ones inherited from the old animation: puller travel
124 mm, gates 10 mm, the belt's reference distance 560 mm, the offset of a side column 140 mm, the
axis position at which the arm is centred on the belt 266 mm.

**The pitch between pallets is two different quantities, not one.** On the **belt** it is how far the
belt moved between them, so it comes from the stand — `GVL_Config.Conveyor.Dist.PalletOffset`,
through `StandGeometry.PitchMm(config)`, with the old animation's 120 mm only as a fallback until the
configuration is read. That is why `Placements` also takes a `PlcConfigSnapshot`, and why the two
views take it as a parameter from `SystemView`.

In a **side column**, however, the puller holds them pressed against one another, so the pitch is the
pallet itself: `PalletDepthMm = 118`, measured from the drawing (the same as its width). The column
moves **all at once** when the puller has pushed it forward — if only the first pallet advanced,
there would be a gap no puller can close.

And the two ends of the column are tied to each other:
`ColumnFrontMm = GripperRowMm - PullerReachMm`, that is the place the arm picks from, minus the
puller travel. Checked against the drawing, the front edge of the pallet falls 4 mm from the green
face of the pusher (the green bar sits at 681…691 mm, the pallet reaches with its edge to 681) — so
it no longer covers the puller, it sits behind it, as it should.

**The pistons have two end sensors, so three states, not two.** Retracted sensor made: it is
retracted. Extended sensor made: it is extended. **Neither made: where it is, is not known** — it is
either travelling or stuck on the way. The old rule, `extended && !retracted`, put the third state in
the same bucket as "retracted", so a puller jammed halfway was drawn identically to one that had got
home. Now the unknown position is drawn at half travel, where no piston ever stops, so it is visible
at a glance.

The pneumatic head is still on that same old rule (`ArmExtended && !ArmRetracted`), and has the same
problem. The storage gates have a single sensor, `Retracted`, so for them there is no "on the way" at
all — there is nothing to tell apart there.

**The four storage gates frame one pallet place** (the pallet sits at x 316…434, y 193…311):

```
                     x 335…415
         y 162…202   ▬▬▬▬▬▬▬▬▬   gate-upper-up      horizontal bars,
    x 285…325  ▮                        ▮  x 425…465   the Bottom signals
    gate-lower-left           gate-lower-right      vertical bars,
         y 302…342   ▬▬▬▬▬▬▬▬▬   gate-upper-down      the Top signals
```

**The shape of a bar and the axis it moves along are different things, and easy to confuse** — it was
confused once and cost a trip: every bar is pulled **perpendicular to its own length**, so the bars
**drawn horizontally** are precisely the ones that move along the system's **vertical axis**. Those
are the `Storage_Gate_Top_*` gates (the `gate-upper-*` files). The ones drawn vertically, at the
sides, move horizontally and are `Storage_Gate_Bottom_*` (the `gate-lower-*` files).

The file names do not help: "upper" / "lower" come from the old visualization and say where they sit
in the picture.

What has to be known and does not show in the files:

- **The front view has no depth.** Every pallet sits on the rail, at the same height; the only thing
  telling them apart is the column's sideways offset. Every drawing in that view has the same top
  margin, so the resting positions had been set by hand in the CODESYS designer and cannot be
  recovered from the SVGs. An attempt at perspective (pallets lower and larger the further forward
  they are) was rejected.
- **The layer order in the front view says where the arm comes from.** It reaches from the front of
  the cell, so it passes **over the side columns** and goes **behind** the pallet on the belt, the
  only one it actually comes down into. Hence the order:

  1. the side columns, whole pallets;
  2. the back wall of the pallets on the belt;
  3. the head: vacuum, its piece, gripper, then the pallet in the gripper with the claw sandwich;
  4. the front wall of the pallets on the belt;
  5. the pistons and the lower rail.

  Two things had been broken while the head sat over everything: the gripper looked as if it went
  into the pallet in the left column when it came down for the vacuum, and a pallet just placed into
  a column looked as if it slid under the one in front of it, right up to the moment it was released
  from the gripper. `Placement.OnBelt` is what separates the two cases.

  Only the pallets on the belt split into two sibling elements, so each half has a key of its own
  (`row-back-<id>` / `row-fore-<id>`); the rest stay `row-<id>`, whole.
- **Approach along the belt is said with light, not with perspective.** The front view has no depth
  to move a pallet through, and perspective was rejected — so the pallet on the belt sits in shadow
  at the far end and comes out of it as it advances towards the arm
  (`StandGeometry.ShadeWithDepth`). Two numbers tune it: `ShadowFloor` (0.35, how dark it is at the
  far end — not zero, it is a pallet somebody is counting) and `GripperRowMm`, where it counts as
  fully arrived.

  It is `filter: brightness`, not `opacity`, and not by accident: the pallet on the belt is drawn in
  two halves with the arm between them, and two transparent halves would show through each other
  along the seam. Two darkened ones still cover each other properly.
- **The numbers taken from the SVGs**, against which any adjustment in the front view is checked
  (750 x 500 mm on a canvas of 2125.98 x 1417.32, that is 2.83464 units per millimetre):

  | what | where |
  |---|---|
  | tip of the claws, at rest | 259.7 mm |
  | top edge of the pallet | 315.6 mm |
  | body of the pallet | 315.6 … 348.1 mm |
  | piece in a slot, centre | 331.1 mm |
  | piece on the vacuum, centre | 284 mm |

  They also show why **lengthening the head travel was a mistake**. The claws stopped at 299.7 mm,
  that is 16 mm above the pallet, and the first attempt was to raise `HeadDropMm` from 40 to 65. But
  the travel is one and the same for the whole head, so the gripper only looked properly low at the
  price of everything else going too deep everywhere. What was wrong was **where the held pallet
  hangs**: at `GripperHangMm = -25` it sat 31 mm below the claws that were supposed to be holding it.
  It is now -66, that is the claws go 10 mm into it, and the travel stayed the real one, 40 mm.

  The rule, for next time: when something does not line up with something else, **move the object,
  not the shared movement**. The travel is shared by the gripper, the claws and the vacuum at once.
- **Seen from above, the arm and its rail stay over the pallets**, as they do in reality, but with
  reduced `opacity` — the `see-through` class. They were like that in the old visualization too.
- **`Unknown.svg` from the original set means NoColor**, and `Unverified.svg` means an unscanned
  pallet (`ObjectType.Unknown`). The black pieces did not exist and are generated on the same circle
  geometry, with the ARGB palette.
- **`Base Rails Standard.svg`** is the variant for the present system; `Base Rails.svg` was the one
  for the upgraded system, with pushers. The pushers, `Vacuum_Extended/Retracted` and
  `Gripper_Extended/Retracted` from the old code do not exist on this stand: there is a single
  vertical piston, `Arm_Extended` / `Arm_Retracted`, which lowers the gripper, the claws and the
  vacuum together.
- The simulator starts with the belt distance at 560 mm. Zero would mean a belt reading nothing and
  would pile the whole queue into the robot's arm.

## Open questions

| what | why it matters |
|---|---|
| the scale of the analog inputs | they currently assume a raw word 0..4095 for 0..10 V; if the module is a different one, `Volts()` changes |
| the source of the AUTO / MANUAL mode | it is currently inferred from `Main.Run`; there is no published mode variable |
| **there is no pause flag** | the PLC publishes `Run` and `ResetStarted`, but not "paused". So "stopped" and "paused" look identical from the HMI. Reset is allowed when `Run` is false and it is not resetting — wider than "only while paused", because tighter than that cannot be told apart |
| `GVL_Config.Arm` and `.Conveyor` on ReadWrite | without it the speeds stay read-only. If it is done, the HMI writes **only** the `Motion.*` fields, never the `Pos.*` limits |
| what AO1 and AO2 are physically wired to | writing them is blocked while the system runs, out of caution; the PLC would accept them at any time |
| **the second pallet on the belt** | the queue is drawn with the 120 mm pitch inherited from the old animation, so the pallet with index 1 ends up right under the storage. The drawn pallet is 118 mm, so the geometry ties up — but it is not known whether the belt physically holds a second pallet there. If it does not, either the pitch is different, or the second pallet should not be drawn |

## Build

```bash
dotnet build "ZEM_BoschRexrothSystemByASTI\ZEM_BoschRexrothSystemByASTI.csproj" -f net10.0-windows10.0.19041.0
```

```bash
dotnet build "ZEM_BoschRexrothSystemByASTI\ZEM_BoschRexrothSystemByASTI.csproj" -t:Run -f net10.0-android
```

The three CS0618 warnings in `OpcUaPlcClient.cs` are old, from the OPC UA API marked obsolete.

If you touch the icon or the splash and it seems not to change on the tablet, delete
`obj/Debug/net10.0-android/**/resizetizer` — the incremental build skips them.

The application starts on the simulator (`Setari → Conexiune`, the "Foloseste simulatorul intern"
tick), with four pallets already on the stand, so it can be demonstrated without a PLC.
