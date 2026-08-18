# The existing HMI — what each page displays and how

For rebuilding the HMI as a separate application, over OPC UA. It describes the
interface running on the target today: four CODESYS visualization pages, their
elements, and the PLC logic behind each button.

**Read together with `OPCUA-HMI.md`** (same folder): that holds the published
symbols, the types, the enums with their numeric values and the recipe for
drawing the cell. This holds the rest: what the pages show and what each control
commands. The two are enough — the CODESYS project does not need to be opened.

Extracted from the project's native export, 11.08.2026. The scripts that produced
it are in `_AI Workplace\Software v13\hmi-extract\`.

> **State at 12.08.2026 09:20: everything is published, 755 variables.**
> `HMI` writable, `IOs` with its 29 `%Q` outputs writable, the controllers and
> `Main.Layout` published ReadWrite. See `OPCUA-HMI.md`.
>
> **All four pages can be reproduced in full**, the Pneumatic buttons and RFID
> writing included.
>
> Note, however: "can be written" does not mean "you are allowed to write it".
> `Main.Layout` and the controllers are published writable through a default
> setting of the dialog, not because they should be touched — a write there
> desynchronises the model or commands the axis over the PLC logic. The exact list
> of what to write and what not is in `OPCUA-HMI.md` §5.

---

## 1. The principle that matters most

**The HMI does not command the machine directly. It writes flags, and the logic
runs in the PLC.**

`HMI` is a PROGRAM that runs every cycle, with four methods:
`ArmControl`, `ConveyorControl`, `RfidControl`, `ShowCurrentObjectColor`
(the full code is in the appendix). It does the jog with travel limits, the
power-on timing, `Pause`/`Resume`, `SmartReset`, absolute and relative movement
with a travel check. The pages only raise and drop flags.

For the new HMI that means **no sequence has to be reimplemented**: you write the
same flags and read the same variables. The behaviour stays identical because it
stays in the PLC.

All these methods are called from `HMI.impl` **only while the cell is not
running**:

```iecst
IF NOT Main.Run AND NOT Main.ResetStarted THEN
    RfidControl();  ConveyorControl();  ArmControl();
    Main.ConveyorController.RunDriverMainFunctions();
    Main.ArmController.RunDriverMainFunctions();
END_IF
```

That is why **`Main.Run` shows up as the second variable on almost every button**:
it is the condition that disables manual commands in automatic. The rule is kept
in the new HMI — a jog button pressed while `Run = TRUE` does nothing in the PLC,
so it has to look disabled rather than appear to be broken.

---

## 2. The pages

### 2.1 Home — 136 elements

The main screen: the state of the cell, the sorting table, the cycle commands.

| what is shown | variables | representation |
|---|---|---|
| compressed air OK | `IOs.Air_Presure_Ok` | lamp |
| storage not empty | `IOs.Storage_Not_Empty` | lamp |
| conveyor distance | `HMI.ConveyorDistance` (LREAL) | text |
| arm position | `Main.ArmController.ReadPosition.Position` | numeric field |
| **6 pallets** | `Main.Layout.Pool[0..5]._realId` + `._objectColorsStr[0..3]` | per pallet: id + 4 colour boxes |
| **8 colours** | `HMI.ColorCount[2..9]` | a counter per colour |
| **sorting policy** | `Main.DropObjectPolicyTable[...]` | an editable field next to each colour |
| Start | `HMI.Button_Start` | button, disabled by `Main.Run` |
| Reset | `Main.ResetCommand` | button |
| Pause | `Main.PauseCommand` | button |
| Pause at end of step | `Main.EndStepPauseCommand` | button |
| current colour | `HMI.CurrentObjectColor` (DWORD ARGB) | coloured square |
| analog inputs | `IOs.AI1` as text, `IOs.AI2..AI4` | three needle gauges (90°) |
| analog outputs | `HMI.ValueAO1/2`, `HMI.WriteAO1/2` | field + write button |

**The pallet slots** — `_objectColorsStr[0..3]` are laid out like this (see also
`OPCUA-HMI.md` §3):

```
   2 | 3      the back row
  ---+---
   1 | 0      the front row
```

**The sorting policy**, read by `ScanAndDropAllObjects`:
`0` = leave the object on the pallet, `1` = drop left, `2` = drop right.

**To be checked — this looks like a mistake in the old page.** The
`HMI.ColorCount[2]` counter (NoColor) is placed next to
`Main.DropObjectPolicyTable[0]` (Unknown), not next to index 2. The other pairings
are correct (`ColorCount[3]`↔`[3]` … `[9]`↔`[9]`). The PLC reads
`DropObjectPolicyTable[Layout.InVacuum]`, and `PickObject` never produces
`Unknown` — it turns it into `NoColor`. So that control writes a box nobody reads,
and the policy for colourless objects cannot be set. In the new HMI, bind it to
index **2**.

### 2.2 Motion — 65 elements

Manual command of the two axes. Every button is inactive while `Main.Run` is
true.

| control | writes | disabling condition |
|---|---|---|
| conveyor jog forward / backward | `HMI.ConveyorJogForward` / `...Backward` | `Main.ConveyorController.Busy`, `Main.Run` |
| arm jog left / right | `HMI.ArmJogLeft` / `...Right` | `Main.ArmController.Busy`, `Main.Run` |
| conveyor / arm power | `HMI.ConveyorSetPower` / `HMI.ArmSetPower` | `Main.Run` |
| conveyor / arm stop | `HMI.ConveyorStop` / `HMI.ArmStop` | `...Busy`, `Main.Run` |
| conveyor / arm reset | `HMI.ConveyorReset` / `HMI.ArmReset` | `HMI.*DeactivateInputs`, `Main.Run` |
| absolute position | `HMI.ArmMoveAbsolutePosition` (REAL) | `HMI.ArmWaitForMoveAbsolute` |
| start absolute | `HMI.ArmMoveAbsolute` | `HMI.ArmDeactivateInputs`, `Main.Run` |
| relative movement | `HMI.ArmMoveRelativePosition` (REAL) | `HMI.ArmWaitForMoveRelative` |
| start relative | `HMI.ArmMoveRelative` | `HMI.ArmAllowRelativeMovement`, `HMI.ArmDeactivateInputs`, `Main.Run` |

Displayed: `Main.ArmController.Power.Status` and
`Main.ConveyorController.Power.Status` as lamps; the arm position;
`HMI.ConveyorDistance`.

**The error lamp per axis** gathers five signals into a single indicator:

```
<Axis>.StepError  OR  <Axis>.Jog.Error  OR  <Axis>.MoveAbsolute.Error
                  OR  <Axis>.AxisInterrupt.Error  OR  <Axis>.AxisContinue.Error
```

(for the conveyor, without `MoveAbsolute.Error`). Keep the grouping — the operator
sees a single "axis in error" lamp, the detail is in `Diag`.

**State flags, read rather than written:** `HMI.ArmDeactivateInputs`,
`HMI.ConveyorDeactivateInputs` (the axis is busy), `HMI.ArmAllowRelativeMovement`
(the requested move fits inside the travel), `HMI.ArmWaitForMoveAbsolute` /
`...Relative` (the movement is under way).

### 2.3 Pneumatic — 78 elements

The pneumatics mimic. The pattern is constant: **a lamp for the sensor, a button
for its command**, side by side.

| group | sensors (lamps) | commands (buttons) |
|---|---|---|
| arm | `IOs.Arm_Extended` | `IOs.Arm_Extend_Cmd`, `IOs.Arm_Retract_Cmd` |
| gripper | `IOs.Gripper_Closed` | `IOs.Gripper_Close_Cmd` |
| vacuum | `IOs.Vacuum_Detected` | `IOs.Vacuum_Cmd` |
| pullers | `IOs.Puller_Left_Extended`, `..._Right_Extended`, `..._Left_Retracted`, `..._Right_Retracted` | `IOs.Puller_Extend_Cmd`, `IOs.Puller_Retract_Cmd` |
| storage gates | `IOs.Storage_Gate_Top_Forward_Retracted`, `..._Top_Backward_Retracted`, `..._Bottom_Left_Retracted`, `..._Bottom_Right_Retracted` | `IOs.Storage_Gates_Top_Retract_Cmd`, `IOs.Storage_Gates_Bottom_Retract_Cmd` |
| pallet presence | `IOs.Exist_Forward_Near`, `..._Forward_Far`, `..._Bacward_Near`, `..._Backward_Far` | — |
| general | `IOs.Air_Presure_Ok`, `IOs.Storage_Not_Empty` | — |

(`Exist_Bacward_Near` is spelled that way in the project.)

**The buttons here cannot be reproduced over OPC UA** — see §3.

### 2.4 RFID — 50 elements

| what | variables | representation |
|---|---|---|
| tag present | `IOs.RFID_Present` | lamp |
| signal strength | `IOs.RFID_Signal_Level` | progress bar |
| bytes read | `IOs.RFID_Read_Byte_0..7` | 8 fields, read-only |
| bytes to write | `IOs.RFID_Write_Byte_0..7` | 8 editable fields |
| read | `HMI.RfidRead` | button, disabled by `Main.Run` |
| write | `HMI.RfidWrite` | button, disabled by `Main.Run` |

The page also has a button that calls `Main.RFID_Controller.Read()` directly — a
duplicate of `HMI.RfidRead`, which does the same thing through `RfidControl`. Do
not reproduce it.

---

## 3. What cannot be reproduced identically, and why

**Methods are not symbols.** OPC UA publishes variables; a method call cannot be
made from outside. Three places:

| in the old page | what you do in the new HMI |
|---|---|
| `Main.RFID_Controller.Read()` | write `HMI.RfidRead := TRUE` — the same effect |
| `WHILE Main.ArmController.MoveToPosition(Position := 200) DO ; END_WHILE` (three buttons, Home and RFID) | `HMI.ArmMoveAbsolutePosition := 200` then `HMI.ArmMoveAbsolute := TRUE` |

The `WHILE` loop is a blocking loop written on a button — it would block the PLC
cycle. **Do not reproduce it in any form.** The flag variant is non-blocking and
retracts the arm before moving it, which the loop did not do.

**`IOs` is published read-only.** So:

- **the Pneumatic page becomes a mimic**: the sensors and the command states are
  visible, but the buttons cannot command anything;
- **RFID writing has no path**: the old page wrote `IOs.RFID_Write_Byte_0..7`
  directly. The controller's intended interface is `RFID_Controller.WriteBytes`
  (the comment in the code says so explicitly), and `HMI.RfidWriteValue` exists
  for it — but **it is not wired to anything**. Without a change in the PLC, the
  "write" button sends whatever is already in the process image.

**Two bindings in Motion are dead:** `Main.ArmController.JogVelocity` and
`.MoveToPointVelocity` do not exist in `FB_ArmController`. They were replaced when
the configuration was refactored. The good source, already published:
`GVL_Config.Arm.Motion.JogVelocity` (and `Conveyor.Motion.JogVelocity`).

**The conveyor override** (`SetOverride.Enable`, `.VelFactor`) was written
straight from the page, into the motion block. The controllers are published
read-only, so it is not reproduced.

---

## 4. The risk to keep in mind

**The jog is a level flag and there is no watchdog on the link.**

The old visualization runs on the target: if it goes down, it goes down together
with the PLC. An external HMI is a different matter. If the OPC UA link breaks
with `HMI.ArmJogRight = TRUE`, **the PLC does not find out and the arm keeps
going** until `JogMax`. Nothing in the current code stops that.

Until there is a heartbeat in the PLC — a variable incremented by the HMI, with
the flags dropped when it stops — take account of it in the interface:

- jog buttons **press-and-hold only**, sending `FALSE` on release, on loss of
  focus and on closing the window;
- on disconnection, the HMI tries to write `FALSE` on every command flag before
  giving up;
- do not put the jog on a control that can stay pressed (a keyboard with repeat,
  a touchscreen with a lost touch).

### The flags are levels, not pulses

The button in the old visualization implicitly did "TRUE on press, FALSE on
release". An OPC UA client does not do that by itself, and the PLC clears very few
flags on its own.

**They clear themselves — only four:** `ArmStop` and `ConveyorStop` (when the axis
is no longer busy), `ArmReset` and `ConveyorReset` (when `SmartReset` has
finished).

**The HMI clears them — all the others:**

| flag | what happens if it stays TRUE |
|---|---|
| `ArmJogLeft/Right`, `ConveyorJogForward/Backward` | the axis keeps going, up to the travel limit |
| `ArmSetPower`, `ConveyorSetPower` | forces power on every cycle and cancels the automatic power-off after 2 s |
| `ArmMoveAbsolute` | **the movement restarts forever** — `ArmWaitForMoveAbsolute` comes back on in the next cycle |
| `ArmMoveRelative` | re-arms after finishing (with a 0 offset, but it still starts) |
| `RfidRead`, `RfidWrite` | reads or writes continuously |
| `Button_Start` | the cell restarts after every pause |
| `WriteAO1`, `WriteAO2` | write the analog output every cycle |

The same list is the list to clear on disconnection, plus `ArmStop` /
`ConveyorStop` if they were raised.

---

## 5. Appendix — the code of the HMI program

The rest of the behaviour follows from here. `Main` writes into HMI only once:
`StartCommand := HMI.Button_Start OR IOs.Button_Start`.

### `HMI` (body)

```iecst
ConveyorDistance := Main.ConveyorController.GetCenterDistance();

ShowCurrentObjectColor();

IF NOT Main.Run AND NOT Main.ResetStarted THEN
	RfidControl();
	ConveyorControl();
	ArmControl();

	Main.ConveyorController.RunDriverMainFunctions();
	Main.ArmController.RunDriverMainFunctions();
END_IF

IF WriteAO1 THEN
	IOs.AO1 := REAL_TO_INT(ValueAO1*1.03489 + 39.33);
END_IF

IF WriteAO2 THEN
	IOs.AO2 := REAL_TO_INT(ValueAO2*1.03489 + 39.33);
END_IF

IF IOs.Arm_Extended AND NOT PreviousArmExtended THEN
	CurrentExtensions := CurrentExtensions + 1;
END_IF
PreviousArmExtended := IOs.Arm_Extended;
```

### `HMI.ArmControl`

```iecst
// Jog
IF ArmJogRight AND Main.ArmController.ReadPosition.Position < Main.ArmController.Config.Pos.JogMax THEN
	IF NOT Main.ArmController.Busy AND ArmPowerActiveTimer.Q THEN
		Main.ArmController.Jog.JogForward := TRUE;
	END_IF
	IF ArmPowerInactiveTimer.Q THEN
		Main.ArmController.Power.Enable := TRUE;
	END_IF
ELSE
	Main.ArmController.Jog.JogForward := FALSE;
END_IF

IF ArmJogLeft AND Main.ArmController.ReadPosition.Position > Main.ArmController.Config.Pos.JogMin THEN
	IF NOT Main.ArmController.Busy AND ArmPowerActiveTimer.Q THEN
		Main.ArmController.Jog.JogBackward := TRUE;
	END_IF
	IF ArmPowerInactiveTimer.Q THEN
		Main.ArmController.Power.Enable := TRUE;
	END_IF
ELSE
	Main.ArmController.Jog.JogBackward := FALSE;
END_IF

// Move Absolute
IF ArmMoveAbsolute THEN
	IOs.Arm_Retract_Cmd := TRUE;
	ArmWaitForMoveAbsolute := TRUE;
END_IF
IF ArmWaitForMoveAbsolute THEN
	IF Main.ArmController.MoveToPosition(Position := ArmMoveAbsolutePosition) THEN
		Main.ArmController.OnEndStep();
		ArmWaitForMoveAbsolute := FALSE;
	END_IF
END_IF

// Move Relative
ArmAllowRelativeMovement := (Main.ArmController.ReadPosition.Position + ArmMoveRelativePosition >= Main.ArmController.Config.Pos.TravelMin)
                        AND (Main.ArmController.ReadPosition.Position + ArmMoveRelativePosition <= Main.ArmController.Config.Pos.TravelMax);
IF ArmMoveRelative AND NOT ArmWaitForMoveRelative AND ArmAllowRelativeMovement THEN
	IOs.Arm_Retract_Cmd := TRUE;
	ArmWaitForMoveRelative := TRUE;
	ArmMoveRelativeStartPosition := LREAL_TO_REAL(Main.ArmController.ReadPosition.Position);
	ArmMoveRelativeGoToPosition := ArmMoveRelativeStartPosition + ArmMoveRelativePosition;
END_IF
IF ArmWaitForMoveRelative THEN
	IF Main.ArmController.MoveToPosition(Position := ArmMoveRelativeGoToPosition) THEN
		Main.ArmController.OnEndStep();
		ArmWaitForMoveRelative := FALSE;
		ArmMoveRelativePosition := 0;
	END_IF
END_IF

// Power
IsArmActive := Main.ArmController.Jog.Active OR Main.ArmController.Jog.Done OR ArmJogRight OR ArmJogLeft;
IF ArmNotActiveTimer.Q AND NOT IsArmActive AND NOT Main.ArmController.Busy AND ArmPowerActiveTimer.Q THEN
	Main.ArmController.Power.Enable := FALSE;
END_IF

IF ArmSetPower THEN
	Main.ArmController.Power.Enable := TRUE;
END_IF

IF ArmStop THEN
	IF NOT Main.ArmController.Busy THEN
		ArmStop := FALSE;
	END_IF
	Main.ArmController.Pause();
ELSE
	IF ArmStopPrevious THEN
		Main.ArmController.Resume();
	END_IF
END_IF
ArmStopPrevious := ArmStop;

IF ArmReset THEN
	IF Main.ArmController.SmartReset() THEN
		ArmReset := FALSE;
		Main.ArmController.OnEndStep();
	END_IF
END_IF

ArmDeactivateInputs := Main.ArmController.Busy OR IsArmActive;

// Timers
ArmNotActiveTimer(IN:= NOT IsArmActive, PT:= T#2S);
ArmPowerActiveTimer(IN:= Main.ArmController.Power.Enable, PT:= T#1S);
ArmPowerInactiveTimer(IN:= NOT Main.ArmController.Power.Enable, PT:= T#1S);
```

What to take from it for the interface: the jog stops **by itself** at `JogMin` /
`JogMax`; power comes on with a 1 s delay and goes off after 2 s of inactivity;
absolute movement retracts the arm before starting; `ArmMoveAbsolute` and
`ArmMoveRelative` are level flags — the HMI sets them back to `FALSE` after
raising them, or the movement re-arms.

### `HMI.ConveyorControl`

Identical in structure, without absolute movement and without travel limits:
jog forward/backward, power with the same two timers,
`ConveyorStop` -> `Pause()`/`Resume()`, `ConveyorReset` -> `SmartReset()`,
`ConveyorDeactivateInputs := IsConveyorActive OR Busy`.

### `HMI.RfidControl`

```iecst
IF RfidRead THEN
	WaitForRfidRead := TRUE;
END_IF
IF WaitForRfidRead THEN
	IF Main.RFID_Controller.Read() THEN
		Main.RFID_Controller.OnEndStep();
		WaitForRfidRead := FALSE;
	END_IF
END_IF

IF RfidWrite THEN
	WaitForRfidWrite := TRUE;
END_IF
IF WaitForRfidWrite THEN
	IF Main.RFID_Controller.Write() THEN
		Main.RFID_Controller.OnEndStep();
		WaitForRfidWrite := FALSE;
	END_IF
END_IF

IF NOT Main.RFID_Controller.Busy THEN
	WaitForRfidRead := FALSE;
	WaitForRfidWrite := FALSE;
END_IF
```

### `HMI.ShowCurrentObjectColor`

```iecst
ColorsDWORD[0] := ObjectColorUnknown;   // 16#00FFFFFF
ColorsDWORD[1] := ObjectColorRed;       // 16#FFFF0000
ColorsDWORD[2] := ObjectColorGreen;     // 16#FF00C000
ColorsDWORD[3] := ObjectColorCyan;      // 16#FF00ECFF
ColorsDWORD[4] := ObjectColorGray;      // 16#FFA9A9A9
ColorsDWORD[5] := ObjectColorOrange;    // 16#FFFFA500
ColorsDWORD[6] := ObjectColorWhite;     // 16#FFFFFFFF
ColorsDWORD[7] := ObjectColorBlack;     // 16#FF000000

CurrentObjectColor := ColorsDWORD[Main.MainController.GetObjectColor()-2];
```

The ARGB colours above are the interface palette — use them in the new HMI too, so
that it looks the same. `CurrentObjectColor` is read ready-computed; subtracting 2
ties `ENUM_ObjectType` (2 = NoColor … 9 = Black) to the table.
