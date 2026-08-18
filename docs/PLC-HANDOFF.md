# Handoff — state of the project

The Bosch Rexroth ctrlX sorting cell. CODESYS 3.5 / ctrlX PLC Engineering 2.6.6,
MainTask cyclic 50 ms, watchdog 20 ms.

**The project is in this folder**: `Software v13.project`. It is edited directly,
from the command line — see below.

> **Note.** This document was written next to the CODESYS project and describes
> that working environment. The paths in it point at tools and working trees that
> are **not** part of the HMI repository.

## Where this sits, among everything else

This file sits **next to the project** so that it travels with it if the project
is sent to someone else: here is everything needed to carry the work on, and
nothing more.

| where | what you find |
|---|---|
| `E:\Mecatronica\README.md` | how the disk is organised: stands, years, where everything is |
| `System Rexroth Basic\SISTEM.md` | what the stand is and which version is the live one (this one) |
| `_AI Workplace\_AI Tools\cds\` | the editing tool — **it does not travel with the project** |
| `_AI Workplace\Software v13\` | the working tree and the AI working materials, kept apart so they do not pile up in the project |

If you received only the project folder and want the tools, they are installed
separately: unpack the package and run `cds doctor`. See
`_AI Workplace\_AI Tools\README.md`.

## Where we are

The editing tools are validated and used daily. On top of them, the following was
done:

1. **The halt convention**, applied to all 23 state machines.
2. **A diagnostics system** (`Diag`), installed and wired in everywhere.
3. **The comments rewritten in English**, with the ones that restated the code
   deleted.
4. **A real drive bug**, found and fixed on the machine (see below — it is the
   most valuable piece of information in the whole document).
5. `ResetSystemState` rewritten as a state machine.

**Two things were added in v13.**

**1. The tree splits the code by who is allowed to write in it**, so that students
can work without breaking the library:

```
Application/  1 SRC/     Main (the task + the movement vocabulary), HMI
              3 SETUP/   GVL_Config, IOs — the stand's numbers
              4 SISTEM/  CheckFunctions
              5 LIB/     Controllers, Models, Diag — not to be touched
```

The numeric prefixes are necessary: the tree sorts alphabetically, and without
them the student's area would end up under everything they must not touch.

**2. `FB_CellState`** (`5 LIB/Models`) — the state of the cell in a single object:

```
VAR_INPUT  IsAtFront[-1..1]     the front pallet is out at the pullers
           InGripper            VirtualId in the gripper, -1 = empty
           InVacuum             the object on the vacuum, Missing = empty
           PalletCount          used entries in the pool
           DroppedCount[-1..1]  objects dropped into each side bin
VAR        Pool[0..5]           identity and contents
           Rows[-1..1]          the position, one queue per column (FB_PalletBuffer)
```

Two layers kept apart: `Pool` holds the identity and the contents, `Rows` holds
the position. A queue holds `VirtualId`s, not pallets, so a pallet carries its
contents wherever it is moved. The columns are `ENUM_Region`: -1 left, 0 the belt,
1 right — **the belt is column 0**, not a separate structure.

**The rule:** the bookkeeping is done **only in the movement methods in `Main`**
(`PickPallet`, `PlacePallet`, `PickObjectFromSidePallet`, `DropObject`, ...),
never in `MainTask` and never in the controllers. The task says what to do, the
vocabulary records what happened. Two greps check this:

```
grep -rn GVL_Config "5 LIB/Controllers"   ->  empty
grep -rn "Main\."   "5 LIB/Controllers"   ->  empty
```

The fields written from outside live in `VAR_INPUT`, not in `VAR`: CODESYS allows
reading from a block's own `VAR`, but not assignment to it. Writes into pallets go
through a local `REFERENCE`, for the same reason.

**Nothing in the model blocks the machine.** If the model does not know about a
pallet, `UnloadFront` returns `-1` and simply nothing is recorded — but `-1` must
never end up as an index: `Pool[-1]` goes through `CheckBounds` and **halts the
application**. Every place that indexes with an id checks it first.

What is **not** done yet: the automatic repair for when the model does not know
about a pallet (the operator can place one by hand on any of the three columns),
the geometry guards, and `Diag` wired into the HMI. See the table below.

## Reading order for a new session

| file | what it holds | when |
|---|---|---|
| `E:\Mecatronica\CLAUDE.md` | permanent context — **it is read automatically**, no need to ask | by itself |
| this file | the real state | first |
| `OPCUA-HMI.md` (here) | what the PLC publishes over OPC UA and how the cell is drawn | if you work on the HMI — **it is enough on its own**, the project does not need opening |
| `ARHITECTURA.md` (here) | analysis + a 9-stage plan | **careful: partly executed and out of date in places** — read it as analysis, not as the current plan |
| `_AI Workplace\_AI Tools\cds\codesys\README.md` | how the project is edited | before any modification |

**The source of truth is the project.** Do a `checkout` and read from the working
tree.

## How to work

**Keep the project open in the IDE and start the daemon once**, at the beginning
of the session — from there everything happens on the fly, without restarting
anything:

```
Tools -> Scripting -> Execute Script File...
E:\Mecatronica\_AI Workplace\_AI Tools\cds\codesys\cds_daemon.py
```

```bash
python "E:\Mecatronica\_AI Workplace\_AI Tools\cds\cds.py" session
```
```bash
python "E:\Mecatronica\_AI Workplace\_AI Tools\cds\cds.py" checkout "E:\Mecatronica\System Rexroth Basic\Workplace 2026\Software v13\Software v13.project" -d "E:\Mecatronica\_AI Workplace\Software v13\work" --force
```
```bash
python "E:\Mecatronica\_AI Workplace\_AI Tools\cds\cds.py" commit -d "E:\Mecatronica\_AI Workplace\Software v13\work"
```

- `checkout` **1.9 s**, a full `commit` (apply + build + save) **4.9 s**.
  Without the daemon and without the IDE open, the same commands work as before
  (~15 s and 40-60 s) — the path is chosen automatically and it prints which one
  it took each time. `--no-live` forces the old path.
- `checkout` leaves the tree **exactly** like the project: it also deletes files
  left over from an earlier checkout (objects moved or renamed in the GUI).
- `commit` sends only what changed, compiles, and saves only if the build is
  clean. If the build fails, **it puts the text back in the IDE too** — it does
  not leave you with broken code on screen.
- **You can edit in the IDE at the same time.** An indicator appears in the top
  right corner (green = free, red = do not edit now), and if you did edit an
  object the tools wanted to write, `commit` refuses everything and tells you
  which — nothing you wrote is lost.
- **New objects**: `commit` cannot create them. There is `cds.py new -c spec.json`
  (`gvl` | `dut` | `fb` | `prg` | `fun` | `method`), idempotent. After `new`, run
  `checkout` again.
- On the old path the project **must not** be open in the GUI (`.project.~u`;
  `cds.py` refuses by itself). On the live path it has to be.

## What has to be known about the axis (learned the hard way, on the machine)

A `Pause()` does **not stop** the movement, it **suspends** it: the axis is left
with an unfinished move. `MC_Reset` does not throw it away (it is not an error),
and cutting the power while the axis still holds it **faults the drive**.

`FB_ArmController.SmartReset` therefore has an order that is not up for
negotiation:

```
0:  clear the commands, BUT leave AxisInterrupt set
5:  MC_Stop            -> cancels the suspended move
10: release Stop, only NOW AxisInterrupt := FALSE
20: MC_Reset           -> the drive still has to be POWERED
25: wait ResetSettleTime (Done does not mean the drive has settled)
30: Power.Enable := FALSE
```

All three matter and none of them works alone. Several hours were lost trying them
two at a time. **Do not reorder these steps without testing on the machine.**

The symptom, if someone breaks it: Stop in the middle of a movement, then Reset ->
drive in error. Stop at the end of a movement works (nothing is left suspended) —
that is the test that tells the cause apart.

Related: `MoveToPosition` waits for `Power.Status` in both directions (steps 5 and
15), so that `Enable` is not pulsed for a single cycle.

## Diagnostics — how to read a halt

`Application/5 LIB/Diag/`: `ST_Diag`, `FB_Diag` (`Report` / `Tick` / `Clear` /
`ReportHalt`), `GVL_Diag` (the global instance `Diag`), `ENUM_Halt` (codes,
`qualified_only`).

All 23 state machines have the same `ELSE`:

```iecst
ELSE
    // Halt: keep the step. It is the only evidence of what went wrong.
    HaltStep := <StepVar>;
    Diag.Report(Source := '<FB>.<Method>', Step := <StepVar>, Code := 0);
    StepError := TRUE;
END_CASE
```

**`ELSE` never resets the step** — the step is the only evidence. `HaltStep` is
sticky (it survives `OnEndStep`). `Report` deduplicates: without that, a halted
sequence would fill the 32-entry buffer in ~1.6 s.

Codes in `ENUM_Halt`, always negative, qualified access
(`ENUM_Halt.H_INVALID_ARG`): `-1` no pullers, `-2` gripper shut, `-10` invalid
arg, `-14` pool full, `-20` timeout, `-30` drive error. An enum, not `INT`
constants, so that `Diag.Last.Code` reads online by name and not as `-14`.

**The rule:** never jump into a non-existent label. A jump into a step that does
not exist means a typing mistake, not an intentional halt — and only that way can
the two be told apart, by a person and by an automatic checker.

**Not wired into the HMI yet.** To be displayed: `Diag.Active`,
`Diag.Last.Source`, `Diag.Last.Step`, `Diag.Last.Cycle`, `Diag.Count`,
`Diag.History[0..31]`.

## What comes next, in order of value

| # | what | why |
|---|---|---|
| 1 | **The HMI** — worked on separately, over OPC UA | Symbol Configuration is **done** (10.08.2026): 289 variables, only the four commands writable. Everything needed to write the interface is in `OPCUA-HMI.md`. What remains to be decided is whether `GVL_Config` moves to ReadWrite, for re-teaching the positions from the HMI |
| 2 | **Wiring `Diag` into the HMI** | `Diag` is published in full; from the moment the model is allowed to assume, an unseen warning is as bad as a silent halt |
| 3 | **"Smart" tracking** — a switch in `GVL_Config`, repair on all three columns, `RepairCount` / `LastRepair` | the operator can place a pallet by hand on -1, 0 or 1; today the model simply records nothing. `Diag` needs severities first (`Warn()`), or a repair lights `Active` and looks like an error |
| 4 | **`check_st.py`** — a static checker | it runs outside the PLC, zero risk. It catches: a jump into a non-existent label (one that is not a declared `H_*` constant), a step `IF` with no `ELSE`, `(x > a) OR (x < b)` as a range check, indexing with `>` instead of `>=`, an unassigned return type |
| 5 | **`FB_Step`** — a sequencer with timeout and auto-reset | the last source of "a machine stuck mute". **Composition, not inheritance** — see the discussion in `ARHITECTURA.md` §5.3, but the agreed variant is a member FB, not `EXTENDS` |
| 6 | A `Diag` -> `LogAdd2` bridge (`CmpLog`) | `Diag` is live state for the HMI and the code; the runtime journal adds what it does not have — real time and survival across a restart. A single call, **after** the deduplication in `Report`, so once per new halt, not once per cycle |
| 7 | `IsConsistent()` on `FB_CellState` | the sum across the columns + gripper + inactive = `PalletCount`. Bookkeeping does not fail by blocking, it fails by lying quietly |
| 8 | Typed tables (`DropRegionByColor`, `ObjectTypeToArgb`) | they work by luck today; the first colour added breaks them silently |
| 9 | `Init()` in `FB_MainController`; the HMI to stop reaching into the driver | it breaks the `Main <-> Controllers` cycle and opens up a generic axis panel |

**The tooling track: DONE** (08.08.2026). The editing cycle no longer restarts the
IDE: `checkout` 1.5 s, `commit` ~2.5 s. Reading comes through the REST API of the
open instance, writing through a daemon living inside it. What turned out
differently from the plan is in `_AI Tools\cds\codesys\PLAN-daemon.md`; how to use
it, in `_AI Tools\cds\codesys\README.md`.

**`pull2src.py` is gone**: `src/` no longer exists as a parallel source,
`checkout` takes the tree straight out of the project.

**What was decided NOT to do** (see the discussion): `PRG_Composition` (stage 5 in
`ARHITECTURA.md`) — high risk, small benefit on a cell with a single arm; replaced
by `Init()`. `I_MachineIO` + `FB_SimulatedIO` and `I_EndEffector` — postponed.
`I_Logger` as an interface — no, a single implementation.

**The agreed rule:** an interface is introduced when there is a second real
implementation, not pre-emptively. And the objective: **at the competition, a
single POU should have to be rewritten.**

## The principle every change is judged by

The code has two populations, with different standards:

- **the library** (drivers, sequencers, model) — written once, calmly: OOP,
  correctness, reuse;
- **the task layer** (`Main.MainTask` + the composition methods) — written **at the
  competition, against the clock, possibly by a beginner**: a linear `CASE`, no
  inheritance, no interfaces.

The test for any pattern: *does it make the file written on competition day
shorter or clearer?* If it only makes the library prettier — it gets done, but
late. If it makes competition day harder — it is refused.

The "unused" methods in `Main` (`PickAndPlaceObject`, `PickObjectFromSidePallet`,
`RFID_Read`, ...) are **not dead code** — they are the movement vocabulary the
task is composed from at the competition. `MainTask` is this year's task.

## Constraints that are not up for negotiation

- **An automatic backup before every write.** `.project` is binary. Implemented: a
  timestamped copy in `_AI Tools\cds\state\backups\<project>\`, made before
  anything else; the IDE side refuses to work if it is missing, and checks on
  every request. The last 20 per project are kept — with the 5-second cycle,
  unlimited copies mean tens of MB a day.
- **No login and no download to the PLC.** No such call exists in the code.
- **Code that does not compile is not saved** (`--save if_build_ok`, the default).
- **Not opened in the GUI and headless at the same time.**

## Where the files are

**Next to the project** — only what is needed to carry the work on, and nothing
more:

```
Software v13/
  Software v13.project       the project
  HANDOFF.md                 this file
  OPCUA-HMI.md               the published symbols + how the cell is drawn
  ARHITECTURA.md             analysis (partly executed and out of date in places)
```

**The tools do not travel with the project.** They live in
`E:\Mecatronica\_AI Workplace\_AI Tools\cds\` and are installed separately: unpack
the package and run `cds doctor`. What it contains and how to uninstall it is
written in the `README.md` there. The automatic backups pile up in
`_AI Tools\cds\state\backups\<project>\`.

**The working tree** (the code extracted as `.st` files, rebuilt at any time with
`checkout`): `E:\Mecatronica\_AI Workplace\Software v13\work`. It does not sit
next to the project, so that the project can be sent out clean.

## How to start a new session

`CLAUDE.md` loads by itself, so the base context is there from the start. The
opening prompt only needs to ask for the state and the intent:

> We are continuing the work on Software v13. Read `HANDOFF.md` from
> `System Rexroth Basic\Workplace 2026\Software v13\`.
> I have the project open in the IDE and the daemon started. The working tree is
> in `_AI Workplace\Software v13\work` — do a `checkout` and read from there. Do
> not modify anything until you confirm what we want to do.

Before that, in the IDE: open the project and run the daemon once from
`_AI Workplace\_AI Tools\cds\codesys\cds_daemon.py`.
Check with `cds.cmd doctor`. Without the daemon everything works the same, only
slower.

**The session's working directory has to be `E:\Mecatronica`** — both `CLAUDE.md`
and the session memory are tied to it.
