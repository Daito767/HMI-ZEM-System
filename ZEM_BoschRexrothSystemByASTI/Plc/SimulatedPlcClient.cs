namespace ZEM_BoschRexrothSystemByASTI.Plc;

/// <summary>
/// A cell that behaves like the real one, so the HMI can be built, shown and demonstrated without
/// a PLC on the bench. It obeys the same rules the PLC does: a queue holds VirtualIds and not
/// pallets, the pool holds identity and content, the pullers are one command for both sides.
///
/// It is not a model of the machine and it is not used for anything but the interface.
/// </summary>
public sealed class SimulatedPlcClient : IPlcClient
{
    private enum Phase
    {
        Feed,
        Scan,
        PickPallet,
        PlacePallet,
        PushOut,
        PickObject,
        DropObject,
        Retract
    }

    private static readonly TimeSpan PhaseTime = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan ResetTime = TimeSpan.FromSeconds(2);

    private static readonly ObjectType[] Palette =
    {
        ObjectType.Red, ObjectType.Green, ObjectType.Cyan, ObjectType.Gray,
        ObjectType.Orange, ObjectType.White, ObjectType.Black, ObjectType.Missing
    };

    private readonly object _lock = new();
    private readonly Random _random = new(20260810);
    private readonly CellSnapshot _cell = new();
    private readonly DiagSnapshot _diag = new() { History = CreateHistory() };
    private readonly PlcConfigSnapshot _config = CreateConfig();
    private readonly MachineSnapshot _machine = new();
    private readonly PolicyState _policies = CreatePolicies();
    private readonly Dictionary<string, bool> _flags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _numbers = new(StringComparer.OrdinalIgnoreCase);

    private double _armTarget = double.NaN;

    private Phase _phase = Phase.Feed;
    private Region _target = Region.Right;
    private DateTime _lastTick = DateTime.UtcNow;
    private TimeSpan _sincePhase;
    private TimeSpan _sinceReset;
    private bool _resetting;
    private bool _pauseAtEndOfStep;
    private int _nextRealId = 1001;

    public SimulatedPlcClient()
    {
        Seed();
    }

    public string Description => "Simulator (fara PLC)";

    public bool IsConnected { get; private set; }

    public IReadOnlyList<SymbolBinding> Bindings { get; } = PlcSymbols.All().Distinct()
        .Select(name => new SymbolBinding(name, "sim://" + name, true, "simulat"))
        .ToList();

    public Task ConnectAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            IsConnected = true;
            _lastTick = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        lock (_lock) IsConnected = false;
        return Task.CompletedTask;
    }

    public Task ReadCellAsync(CellSnapshot target, CancellationToken ct)
    {
        lock (_lock)
        {
            Advance();
            CopyInto(target);
        }
        return Task.CompletedTask;
    }

    public Task ReadMachineAsync(MachineSnapshot target, CancellationToken ct)
    {
        lock (_lock)
        {
            Advance();
            CopyMachine(_machine, target);
        }
        return Task.CompletedTask;
    }

    public Task ReadPoliciesAsync(PolicyState target, CancellationToken ct)
    {
        lock (_lock)
        {
            Array.Copy(_policies.DropObject, target.DropObject, PolicyState.ObjectPolicyCount);
            Array.Copy(_policies.DropPallet, target.DropPallet, PolicyState.PalletPolicyCount);
            target.LoadedAt = DateTime.Now;
        }
        return Task.CompletedTask;
    }

    public Task WriteBoolAsync(string symbol, bool value, CancellationToken ct)
    {
        lock (_lock)
        {
            _flags[symbol] = value;
            if (value) OnFlagRaised(symbol);
        }
        return Task.CompletedTask;
    }

    public Task WriteRealAsync(string symbol, double value, CancellationToken ct)
    {
        lock (_lock) _numbers[symbol] = value;
        return Task.CompletedTask;
    }

    public Task WriteIntAsync(string symbol, int value, CancellationToken ct)
    {
        lock (_lock)
        {
            _numbers[symbol] = value;
            ApplyIntWrite(symbol, value);
        }
        return Task.CompletedTask;
    }

    public Task ClearCommandFlagsAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            foreach (var flag in PlcSymbols.CommandFlags.Concat(PlcSymbols.ValveCommands))
                _flags[flag] = false;
        }
        return Task.CompletedTask;
    }

    public Task ReadDiagAsync(DiagSnapshot target, CancellationToken ct)
    {
        lock (_lock)
        {
            target.Active = _diag.Active;
            target.Count = _diag.Count;
            target.Head = _diag.Head;
            target.Cycle = _diag.Cycle;
            target.Last = _diag.Last;
        }
        return Task.CompletedTask;
    }

    public Task ReadDiagHistoryAsync(DiagSnapshot target, CancellationToken ct)
    {
        lock (_lock)
        {
            target.History = _diag.History.Select(e => new DiagEntry
            {
                Source = e.Source, Step = e.Step, Code = e.Code, Cycle = e.Cycle
            }).ToArray();
            target.HistoryLoadedAt = DateTime.Now;
        }
        return Task.CompletedTask;
    }

    public Task ReadColorNamesAsync(CellSnapshot target, CancellationToken ct)
    {
        lock (_lock)
        {
            for (var p = 0; p < CellSnapshot.PoolSize; p++)
                for (var s = 0; s < PalletInfo.SlotCount; s++)
                    target.Pool[p].SlotNames[s] = _cell.Pool[p].Slots[s].ToString();
            target.HasColorNames = true;
        }
        return Task.CompletedTask;
    }

    public Task ReadConfigAsync(PlcConfigSnapshot target, CancellationToken ct)
    {
        lock (_lock)
        {
            CopyConfig(_config, target);
            target.LoadedAt = DateTime.Now;
        }
        return Task.CompletedTask;
    }

    public Task SendCommandAsync(PlcCommand command, CancellationToken ct)
    {
        lock (_lock)
        {
            switch (command)
            {
                case PlcCommand.Start:
                    if (!_resetting) _cell.Run = true;
                    _pauseAtEndOfStep = false;
                    break;
                case PlcCommand.Pause:
                    _cell.Run = false;
                    break;
                case PlcCommand.EndStepPause:
                    _pauseAtEndOfStep = true;
                    break;
                case PlcCommand.Reset:
                    _resetting = true;
                    _sinceReset = TimeSpan.Zero;
                    _cell.Run = false;
                    _cell.ResetStarted = true;
                    break;
            }
        }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // --- the simulated sequence -------------------------------------------------------

    private void Advance()
    {
        var now = DateTime.UtcNow;
        var elapsed = now - _lastTick;
        _lastTick = now;
        if (elapsed > TimeSpan.FromSeconds(2)) elapsed = TimeSpan.FromMilliseconds(50);

        // MainTask is a 50 ms cyclic task.
        _diag.Cycle += (uint)Math.Max(1, elapsed.TotalMilliseconds / 50);

        if (_resetting)
        {
            _sinceReset += elapsed;
            if (_sinceReset < ResetTime) return;
            _resetting = false;
            _cell.ResetStarted = false;
            Seed();
            _diag.Active = false;
            return;
        }

        AdvanceMachine(elapsed);

        if (!_cell.Run) return;

        _sincePhase += elapsed;
        if (_sincePhase < PhaseTime) return;
        _sincePhase = TimeSpan.Zero;

        RunPhase();

        if (_pauseAtEndOfStep && _phase == Phase.Feed)
        {
            _pauseAtEndOfStep = false;
            _cell.Run = false;
        }
    }

    private void RunPhase()
    {
        switch (_phase)
        {
            case Phase.Feed:
                _cell.MainStep = 10;
                Feed();
                _phase = Phase.Scan;
                break;

            case Phase.Scan:
                _cell.MainStep = 20;
                Scan();
                _phase = Phase.PickPallet;
                break;

            case Phase.PickPallet:
                _cell.MainStep = 30;
                _phase = PickPallet() ? Phase.PlacePallet : Phase.PushOut;
                break;

            case Phase.PlacePallet:
                _cell.MainStep = 40;
                PlacePallet();
                _phase = Phase.PushOut;
                break;

            case Phase.PushOut:
                _cell.MainStep = 50;
                PushOut();
                _phase = Phase.PickObject;
                break;

            case Phase.PickObject:
                _cell.MainStep = 60;
                _phase = PickObject() ? Phase.DropObject : Phase.Retract;
                break;

            case Phase.DropObject:
                _cell.MainStep = 70;
                DropObject();
                _phase = Phase.PickObject;
                break;

            case Phase.Retract:
                _cell.MainStep = 80;
                Retract();
                _phase = Phase.Feed;
                break;
        }
    }

    private void Feed()
    {
        var conveyor = _cell.Rows[Region.Center];
        if (conveyor.Count >= conveyor.Capacity) return;

        var free = Array.FindIndex(_cell.Pool, p => !p.IsValid);
        if (free < 0)
        {
            Report("FB_CellState.Allocate", 10, HaltCode.PoolFull);
            return;
        }

        var pallet = _cell.Pool[free];
        pallet.IsValid = true;
        pallet.VirtualId = free;
        pallet.RealId = _random.Next(4) == 0 ? 0 : _nextRealId++;
        for (var s = 0; s < PalletInfo.SlotCount; s++)
            pallet.Slots[s] = ObjectType.Unknown;

        // The magazine feeds the back of the conveyor; index 0 stays at the front.
        conveyor.PalletIds[conveyor.Count] = free;
        conveyor.Count++;
        _cell.PalletCount = _cell.Pool.Count(p => p.IsValid);
    }

    private void Scan()
    {
        var conveyor = _cell.Rows[Region.Center];
        if (conveyor.Count == 0) return;

        var pallet = _cell.PalletById(conveyor.PalletIds[0]);
        if (pallet is null) return;

        for (var s = 0; s < PalletInfo.SlotCount; s++)
            if (pallet.Slots[s] == ObjectType.Unknown)
                pallet.Slots[s] = Palette[_random.Next(Palette.Length)];
    }

    private bool PickPallet()
    {
        if (_cell.GripperHoldsPallet) return true;

        var conveyor = _cell.Rows[Region.Center];
        if (conveyor.Count == 0) return false;

        _cell.InGripper = UnloadFront(conveyor);
        return _cell.InGripper >= 0;
    }

    private void PlacePallet()
    {
        if (!_cell.GripperHoldsPallet) return;

        var left = _cell.Rows[Region.Left];
        var right = _cell.Rows[Region.Right];
        _target = left.Count <= right.Count ? Region.Left : Region.Right;

        var row = _cell.Rows[_target];
        if (row.Count >= row.Capacity)
        {
            Report("Main.PlacePallet", 40, HaltCode.InvalidArg);
            return;
        }

        // The arm places at the front; whatever was there is pushed back one position.
        for (var i = row.Count; i > 0; i--)
            row.PalletIds[i] = row.PalletIds[i - 1];
        row.PalletIds[0] = _cell.InGripper;
        row.Count++;
        _cell.InGripper = -1;
    }

    private void PushOut()
    {
        // One command for both sides: whichever side has a pallet in front goes out.
        foreach (var region in new[] { Region.Left, Region.Right })
            _cell.Rows[region].IsAtFront = _cell.Rows[region].Count > 0;
    }

    private bool PickObject()
    {
        if (_cell.VacuumHoldsObject) return true;

        var row = _cell.Rows[_target];
        if (!row.IsAtFront || row.Count == 0) return false;

        var pallet = _cell.PalletById(row.PalletIds[0]);
        if (pallet is null) return false;

        // While the pallet is out at the pullers, only the back row is reachable.
        foreach (var slot in new[] { 2, 3 })
        {
            if (pallet.Slots[slot] is ObjectType.Missing or ObjectType.Unknown) continue;
            _cell.InVacuum = pallet.Slots[slot];
            pallet.Slots[slot] = ObjectType.Missing;
            return true;
        }

        return false;
    }

    private void DropObject()
    {
        if (!_cell.VacuumHoldsObject) return;

        // The PLC reads DropObjectPolicyTable[InVacuum]: 0 leaves it, 1 drops left, 2 drops right.
        var policy = _policies.DropObject[(int)_cell.InVacuum];
        if (policy is 1 or 2)
        {
            _cell.Rows[policy == 1 ? Region.Left : Region.Right].DroppedCount++;

            var counter = (int)_cell.InVacuum - (int)ObjectType.NoColor;
            if (counter >= 0 && counter < _machine.Hmi.ColorCounts.Length)
                _machine.Hmi.ColorCounts[counter]++;
        }

        _cell.InVacuum = ObjectType.Missing;
    }

    private void Retract()
    {
        foreach (var region in new[] { Region.Left, Region.Right })
            _cell.Rows[region].IsAtFront = false;
    }

    private static int UnloadFront(RowState row)
    {
        if (row.Count == 0) return -1;
        var id = row.PalletIds[0];
        for (var i = 0; i < row.Count - 1; i++)
            row.PalletIds[i] = row.PalletIds[i + 1];
        row.PalletIds[row.Count - 1] = -1;
        row.Count--;
        return id;
    }

    // --- the simulated machine: axes, process image, command layer --------------------

    private bool Flag(string symbol) => _flags.TryGetValue(symbol, out var value) && value;

    private double Number(string symbol) => _numbers.TryGetValue(symbol, out var value) ? value : 0;

    /// <summary>
    /// Mirrors the rule from <c>HMI.impl</c>: the manual commands are only acted on while the cell
    /// is neither running nor resetting.
    /// </summary>
    private bool ManualAllowed => !_cell.Run && !_cell.ResetStarted;

    private void AdvanceMachine(TimeSpan elapsed)
    {
        var seconds = elapsed.TotalSeconds;
        var arm = _machine.Arm;
        var conveyor = _machine.Conveyor;
        var io = _machine.Io;
        var hmi = _machine.Hmi;
        var pos = _config.Arm;

        if (ManualAllowed)
        {
            if (Flag(PlcSymbols.ArmSetPower)) arm.PowerOn = true;
            if (Flag(PlcSymbols.ConveyorSetPower)) conveyor.PowerOn = true;

            if (Flag(PlcSymbols.ArmJogRight) && arm.Position < pos.JogMax)
            {
                arm.PowerOn = true;
                arm.Position = Math.Min(pos.JogMax, arm.Position + 140 * seconds);
            }
            else if (Flag(PlcSymbols.ArmJogLeft) && arm.Position > pos.JogMin)
            {
                arm.PowerOn = true;
                arm.Position = Math.Max(pos.JogMin, arm.Position - 140 * seconds);
            }

            if (Flag(PlcSymbols.ConveyorJogForward))
            {
                conveyor.PowerOn = true;
                hmi.ConveyorDistance += 90 * seconds;
            }
            else if (Flag(PlcSymbols.ConveyorJogBackward))
            {
                conveyor.PowerOn = true;
                hmi.ConveyorDistance = Math.Max(0, hmi.ConveyorDistance - 90 * seconds);
            }

            if (!double.IsNaN(_armTarget))
            {
                arm.PowerOn = true;
                var step = 220 * seconds;
                if (Math.Abs(_armTarget - arm.Position) <= step)
                {
                    arm.Position = _armTarget;
                    _armTarget = double.NaN;
                    hmi.WaitForMoveAbsolute = false;
                    hmi.WaitForMoveRelative = false;
                }
                else
                {
                    arm.Position += Math.Sign(_armTarget - arm.Position) * step;
                }
            }
        }

        arm.Busy = !double.IsNaN(_armTarget);
        arm.InputsDisabled = arm.Busy || Flag(PlcSymbols.ArmJogLeft) || Flag(PlcSymbols.ArmJogRight);
        conveyor.InputsDisabled = Flag(PlcSymbols.ConveyorJogForward) || Flag(PlcSymbols.ConveyorJogBackward);

        var relative = arm.Position + Number(PlcSymbols.ArmMoveRelativePosition);
        hmi.AllowRelativeMovement = relative >= pos.TravelMin && relative <= pos.TravelMax;

        // Process image
        io.AirPressureOk = true;
        io.StorageNotEmpty = _cell.Pool.Any(p => !p.IsValid);
        io.ButtonStart = false;

        var distance = (int)Math.Clamp(hmi.ConveyorDistance, 0, 65535);
        io.DistanceCenter1 = distance / 256;
        io.DistanceCenter2 = distance % 256;

        io.ArmExtendCmd = Flag(PlcSymbols.ArmExtendCmd);
        io.ArmRetractCmd = Flag(PlcSymbols.ArmRetractCmd);
        io.ArmExtended = io.ArmExtendCmd && !io.ArmRetractCmd;
        io.ArmRetracted = !io.ArmExtended;

        io.GripperCloseCmd = Flag(PlcSymbols.GripperCloseCmd);
        io.GripperClosed = io.GripperCloseCmd || _cell.GripperHoldsPallet;
        io.VacuumCmd = Flag(PlcSymbols.VacuumCmd);
        io.VacuumDetected = io.VacuumCmd || _cell.VacuumHoldsObject;

        io.PullerExtendCmd = Flag(PlcSymbols.PullerExtendCmd);
        io.PullerRetractCmd = Flag(PlcSymbols.PullerRetractCmd);
        var pullersOut = io.PullerExtendCmd || _cell.Rows[Region.Left].IsAtFront || _cell.Rows[Region.Right].IsAtFront;
        io.PullerLeftExtended = pullersOut;
        io.PullerRightExtended = pullersOut;
        io.PullerLeftRetracted = !pullersOut;
        io.PullerRightRetracted = !pullersOut;

        io.GatesTopRetractCmd = Flag(PlcSymbols.GatesTopRetractCmd);
        io.GatesBottomRetractCmd = Flag(PlcSymbols.GatesBottomRetractCmd);
        io.GateTopForwardRetracted = io.GatesTopRetractCmd;
        io.GateTopBackwardRetracted = io.GatesTopRetractCmd;
        io.GateBottomLeftRetracted = io.GatesBottomRetractCmd;
        io.GateBottomRightRetracted = io.GatesBottomRetractCmd;

        io.ExistForwardNear = _cell.Rows[Region.Center].Count > 0;
        io.ExistForwardFar = _cell.Rows[Region.Center].Count > 1;
        io.ExistBackwardNear = _cell.Rows[Region.Left].Count > 0;
        io.ExistBackwardFar = _cell.Rows[Region.Right].Count > 0;

        for (var i = 0; i < io.ColorSensors.Length; i++)
            io.ColorSensors[i] = _cell.InVacuum == IoState.ColorSensorTypes[i];

        io.AnalogIn[0] = 2048 + (int)(400 * Math.Sin(_diag.Cycle / 90.0));
        io.AnalogIn[1] = 1500 + (int)(300 * Math.Sin(_diag.Cycle / 140.0));
        io.AnalogIn[2] = 900 + (int)(200 * Math.Sin(_diag.Cycle / 70.0));
        io.AnalogIn[3] = (int)Math.Clamp(arm.Position * 10, 0, 8000);

        var front = _cell.Rows[Region.Center].Count > 0
            ? _cell.PalletById(_cell.Rows[Region.Center].PalletIds[0])
            : null;
        io.RfidPresent = front is { RealId: > 0 };
        io.RfidExistTag = io.RfidPresent;
        io.RfidReady = true;
        io.RfidAntennaEnabled = true;
        io.RfidSignalLevel = io.RfidPresent ? 78 : 0;

        hmi.CurrentObjectColor = PlcColor(_cell.InVacuum);
    }

    /// <summary>The palette from <c>HMI.ShowCurrentObjectColor</c>.</summary>
    private static uint PlcColor(ObjectType type) => type switch
    {
        ObjectType.Red => 0xFFFF0000,
        ObjectType.Green => 0xFF00C000,
        ObjectType.Cyan => 0xFF00ECFF,
        ObjectType.Gray => 0xFFA9A9A9,
        ObjectType.Orange => 0xFFFFA500,
        ObjectType.White => 0xFFFFFFFF,
        ObjectType.Black => 0xFF000000,
        _ => 0
    };

    private void OnFlagRaised(string symbol)
    {
        var arm = _machine.Arm;
        var hmi = _machine.Hmi;

        switch (symbol)
        {
            case PlcSymbols.ArmMoveAbsolute when ManualAllowed:
                _armTarget = Math.Clamp(
                    Number(PlcSymbols.ArmMoveAbsolutePosition), _config.Arm.TravelMin, _config.Arm.TravelMax);
                hmi.WaitForMoveAbsolute = true;
                break;

            case PlcSymbols.ArmMoveRelative when ManualAllowed && hmi.AllowRelativeMovement:
                _armTarget = Math.Clamp(
                    arm.Position + Number(PlcSymbols.ArmMoveRelativePosition),
                    _config.Arm.TravelMin, _config.Arm.TravelMax);
                hmi.WaitForMoveRelative = true;
                break;

            case PlcSymbols.ArmStop:
                _armTarget = double.NaN;
                hmi.WaitForMoveAbsolute = false;
                hmi.WaitForMoveRelative = false;
                _flags[PlcSymbols.ArmStop] = false;
                break;

            case PlcSymbols.ConveyorStop:
                _flags[PlcSymbols.ConveyorStop] = false;
                break;

            case PlcSymbols.ArmReset:
                arm.StepError = arm.JogError = arm.MoveAbsoluteError = false;
                arm.InterruptError = arm.ContinueError = false;
                arm.PowerOn = false;
                _flags[PlcSymbols.ArmReset] = false;
                break;

            case PlcSymbols.ConveyorReset:
                var conveyor = _machine.Conveyor;
                conveyor.StepError = conveyor.JogError = false;
                conveyor.InterruptError = conveyor.ContinueError = false;
                conveyor.PowerOn = false;
                _flags[PlcSymbols.ConveyorReset] = false;
                break;

            case PlcSymbols.RfidRead:
                ReadRfid();
                break;

            case "HMI.WriteAO1":
                WriteAnalogOut(0);
                break;

            case "HMI.WriteAO2":
                WriteAnalogOut(1);
                break;
        }
    }

    /// <summary>The conversion the PLC does: <c>REAL_TO_INT(Value * 1.03489 + 39.33)</c>.</summary>
    private void WriteAnalogOut(int index)
    {
        var value = Number(PlcSymbols.ValueAnalogOut(index + 1));
        _machine.Hmi.AnalogOutValue[index] = value;
        _machine.Io.AnalogOut[index] = (int)Math.Round(value * 1.03489 + 39.33);
        _flags[PlcSymbols.WriteAnalogOut(index + 1)] = false;
    }

    private void ReadRfid()
    {
        var row = _cell.Rows[Region.Center];
        var pallet = row.Count > 0 ? _cell.PalletById(row.PalletIds[0]) : null;
        var id = pallet?.RealId ?? 0;

        for (var i = 0; i < _machine.Io.RfidReadBytes.Length; i++)
            _machine.Io.RfidReadBytes[i] = i < 2 ? (id >> (8 * (1 - i))) & 0xFF : 0;
    }

    private void ApplyIntWrite(string symbol, int value)
    {
        if (TryIndex(symbol, "IOs.RFID_Write_Byte_", out var byteIndex, bracket: false)
            && byteIndex < _machine.Io.RfidWriteBytes.Length)
        {
            _machine.Io.RfidWriteBytes[byteIndex] = value;
            return;
        }

        if (TryIndex(symbol, "Main.DropObjectPolicyTable", out var objectIndex, bracket: true)
            && objectIndex < PolicyState.ObjectPolicyCount)
        {
            _policies.DropObject[objectIndex] = value;
            return;
        }

        if (TryIndex(symbol, "Main.DropPalletePolicyTable", out var palletIndex, bracket: true)
            && palletIndex < PolicyState.PalletPolicyCount)
        {
            _policies.DropPallet[palletIndex] = value;
        }
    }

    private static bool TryIndex(string symbol, string prefix, out int index, bool bracket)
    {
        index = -1;
        if (!symbol.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var rest = symbol[prefix.Length..];
        if (bracket) rest = rest.Trim('[', ']');
        return int.TryParse(rest, out index) && index >= 0;
    }

    private static PolicyState CreatePolicies()
    {
        var policies = new PolicyState();
        // Something plausible: warm colours left, cool colours right, unscanned stays on the pallet.
        policies.DropObject[(int)ObjectType.Red] = 1;
        policies.DropObject[(int)ObjectType.Orange] = 1;
        policies.DropObject[(int)ObjectType.Black] = 1;
        policies.DropObject[(int)ObjectType.Green] = 2;
        policies.DropObject[(int)ObjectType.Cyan] = 2;
        policies.DropObject[(int)ObjectType.White] = 2;
        policies.DropObject[(int)ObjectType.Gray] = 2;
        return policies;
    }

    private static void CopyMachine(MachineSnapshot source, MachineSnapshot target)
    {
        CopyAxis(source.Arm, target.Arm);
        CopyAxis(source.Conveyor, target.Conveyor);

        var a = source.Io;
        var b = target.Io;
        b.ArmExtended = a.ArmExtended; b.ArmRetracted = a.ArmRetracted;
        b.ArmExtendCmd = a.ArmExtendCmd; b.ArmRetractCmd = a.ArmRetractCmd;
        b.GripperClosed = a.GripperClosed; b.GripperCloseCmd = a.GripperCloseCmd;
        b.VacuumDetected = a.VacuumDetected; b.VacuumCmd = a.VacuumCmd;
        b.PullerLeftExtended = a.PullerLeftExtended; b.PullerRightExtended = a.PullerRightExtended;
        b.PullerLeftRetracted = a.PullerLeftRetracted; b.PullerRightRetracted = a.PullerRightRetracted;
        b.PullerExtendCmd = a.PullerExtendCmd; b.PullerRetractCmd = a.PullerRetractCmd;
        b.GateTopForwardRetracted = a.GateTopForwardRetracted; b.GateTopBackwardRetracted = a.GateTopBackwardRetracted;
        b.GateBottomLeftRetracted = a.GateBottomLeftRetracted; b.GateBottomRightRetracted = a.GateBottomRightRetracted;
        b.GatesTopRetractCmd = a.GatesTopRetractCmd; b.GatesBottomRetractCmd = a.GatesBottomRetractCmd;
        b.ExistForwardNear = a.ExistForwardNear; b.ExistForwardFar = a.ExistForwardFar;
        b.ExistBackwardNear = a.ExistBackwardNear; b.ExistBackwardFar = a.ExistBackwardFar;
        b.AirPressureOk = a.AirPressureOk; b.StorageNotEmpty = a.StorageNotEmpty; b.ButtonStart = a.ButtonStart;
        b.DistanceCenter1 = a.DistanceCenter1; b.DistanceCenter2 = a.DistanceCenter2;
        b.RfidPresent = a.RfidPresent; b.RfidExistTag = a.RfidExistTag; b.RfidReady = a.RfidReady;
        b.RfidError = a.RfidError; b.RfidAlarm1 = a.RfidAlarm1; b.RfidAlarm2 = a.RfidAlarm2;
        b.RfidAntennaEnabled = a.RfidAntennaEnabled; b.RfidStatusByte = a.RfidStatusByte;
        b.RfidSignalLevel = a.RfidSignalLevel;
        Array.Copy(a.ColorSensors, b.ColorSensors, a.ColorSensors.Length);
        Array.Copy(a.AnalogIn, b.AnalogIn, a.AnalogIn.Length);
        Array.Copy(a.AnalogOut, b.AnalogOut, a.AnalogOut.Length);
        Array.Copy(a.RfidReadBytes, b.RfidReadBytes, a.RfidReadBytes.Length);
        Array.Copy(a.RfidWriteBytes, b.RfidWriteBytes, a.RfidWriteBytes.Length);

        var h = source.Hmi;
        var t = target.Hmi;
        t.ConveyorDistance = h.ConveyorDistance;
        t.CurrentObjectColor = h.CurrentObjectColor;
        t.AllowRelativeMovement = h.AllowRelativeMovement;
        t.WaitForMoveAbsolute = h.WaitForMoveAbsolute;
        t.WaitForMoveRelative = h.WaitForMoveRelative;
        Array.Copy(h.ColorCounts, t.ColorCounts, h.ColorCounts.Length);
        Array.Copy(h.AnalogOutValue, t.AnalogOutValue, h.AnalogOutValue.Length);

        target.UpdatedAt = DateTime.Now;
    }

    private static void CopyAxis(AxisState source, AxisState target)
    {
        target.Position = source.Position;
        target.PowerOn = source.PowerOn;
        target.Busy = source.Busy;
        target.StepError = source.StepError;
        target.JogError = source.JogError;
        target.MoveAbsoluteError = source.MoveAbsoluteError;
        target.InterruptError = source.InterruptError;
        target.ContinueError = source.ContinueError;
        target.InputsDisabled = source.InputsDisabled;
    }

    private void Report(string source, int step, HaltCode code)
    {
        var entry = new DiagEntry { Source = source, Step = step, Code = code, Cycle = _diag.Cycle };

        // Report deduplicates: the same halt does not fill the buffer once a second.
        if (_diag.Last.Source == source && _diag.Last.Step == step && _diag.Last.Code == code)
            return;

        _diag.Last = entry;
        _diag.Active = true;
        _diag.History[_diag.Head] = entry;
        _diag.Head = (_diag.Head + 1) % DiagSnapshot.HistorySize;
        _diag.Count = Math.Min(_diag.Count + 1, DiagSnapshot.HistorySize);
    }

    // --- state helpers ----------------------------------------------------------------

    private void Seed()
    {
        foreach (var region in PlcEnums.AllRegions)
        {
            var row = _cell.Rows[region];
            row.Count = 0;
            row.Capacity = RowState.MaxCapacity;
            row.IsAtFront = false;
            row.DroppedCount = 0;
            Array.Fill(row.PalletIds, -1);
        }

        foreach (var pallet in _cell.Pool)
        {
            pallet.IsValid = false;
            pallet.RealId = 0;
            Array.Fill(pallet.Slots, ObjectType.Missing);
            Array.Fill(pallet.SlotNames, string.Empty);
        }

        _cell.InGripper = -1;
        _cell.InVacuum = ObjectType.Missing;
        _cell.MainStep = 0;

        // Belt distance, the one number the stand drawing places the pallet queue by. Zero would be
        // a belt reading nothing, which piles the whole queue against the arm.
        _machine.Hmi.ConveyorDistance = 560;
        _phase = Phase.Feed;
        _sincePhase = TimeSpan.Zero;
        _nextRealId = 1001;

        // A stopped cell is not an empty cell. Starting with pallets on the stand means the HMI shows
        // something real before anyone presses Start.
        Place(0, Region.Left, 1001, ObjectType.Red, ObjectType.Green, ObjectType.Missing, ObjectType.Cyan);
        Place(1, Region.Center, 0, ObjectType.Missing, ObjectType.Missing, ObjectType.Orange, ObjectType.White);
        Place(2, Region.Center, 1003, ObjectType.Unknown, ObjectType.Unknown, ObjectType.Unknown, ObjectType.Unknown);
        Place(3, Region.Right, 1004, ObjectType.Black, ObjectType.Gray, ObjectType.Red, ObjectType.Missing);
        _nextRealId = 1005;

        _cell.PalletCount = _cell.Pool.Count(p => p.IsValid);
    }

    /// <summary>Puts a pool entry at the back of a column, the way the magazine would.</summary>
    private void Place(int poolIndex, Region region, int realId, params ObjectType[] slots)
    {
        var pallet = _cell.Pool[poolIndex];
        pallet.IsValid = true;
        pallet.VirtualId = poolIndex;
        pallet.RealId = realId;
        Array.Copy(slots, pallet.Slots, Math.Min(slots.Length, PalletInfo.SlotCount));

        var row = _cell.Rows[region];
        if (row.Count >= row.Capacity) return;
        row.PalletIds[row.Count] = poolIndex;
        row.Count++;
    }

    private void CopyInto(CellSnapshot target)
    {
        target.Run = _cell.Run;
        target.ResetStarted = _cell.ResetStarted;
        target.MainStep = _cell.MainStep;
        target.InGripper = _cell.InGripper;
        target.InVacuum = _cell.InVacuum;
        target.PalletCount = _cell.PalletCount;

        foreach (var region in PlcEnums.AllRegions)
        {
            var source = _cell.Rows[region];
            var row = target.Rows[region];
            row.Count = source.Count;
            row.Capacity = source.Capacity;
            row.IsAtFront = source.IsAtFront;
            row.DroppedCount = source.DroppedCount;
            Array.Copy(source.PalletIds, row.PalletIds, RowState.MaxCapacity);
        }

        for (var p = 0; p < CellSnapshot.PoolSize; p++)
        {
            var source = _cell.Pool[p];
            var pallet = target.Pool[p];
            pallet.IsValid = source.IsValid;
            pallet.VirtualId = source.VirtualId;
            pallet.RealId = source.RealId;
            Array.Copy(source.Slots, pallet.Slots, PalletInfo.SlotCount);
        }

        target.UpdatedAt = DateTime.Now;
    }

    private static DiagEntry[] CreateHistory() =>
        Enumerable.Range(0, DiagSnapshot.HistorySize).Select(_ => new DiagEntry()).ToArray();

    /// <summary>Numbers in the shape of the real stand, so the drawing has something to scale to.</summary>
    private static PlcConfigSnapshot CreateConfig()
    {
        var config = new PlcConfigSnapshot();

        config.Arm.Home = 0;
        config.Arm.PalletLeft = 120;
        config.Arm.PalletCenter = 350;
        config.Arm.PalletRight = 580;
        config.Arm.SlotLeft = 40;
        config.Arm.SlotRight = 660;
        config.Arm.DropLeft = 20;
        config.Arm.DropRight = 680;
        config.Arm.ColorSensor = 300;
        config.Arm.TravelMin = 0;
        config.Arm.TravelMax = 700;
        config.Arm.JogMin = 10;
        config.Arm.JogMax = 690;

        config.Arm.MoveVelocity = 400;
        config.Arm.MoveAccel = 2000;
        config.Arm.MoveDecel = 2000;
        config.Arm.MoveJerk = 20000;
        config.Arm.JogVelocity = 80;
        config.Arm.JogAccel = 500;
        config.Arm.JogDecel = 500;
        config.Arm.JogJerk = 5000;
        config.Arm.StopDecel = 3000;

        config.Arm.VacuumDetectionTimeout = TimeSpan.FromMilliseconds(1500);
        config.Arm.ResetSettleTime = TimeSpan.FromMilliseconds(300);
        config.Arm.ResetStopTimeout = TimeSpan.FromMilliseconds(2000);
        config.Arm.KeepPoweredAfterMove = false;

        config.Conveyor.FirstRow = 180;
        config.Conveyor.SecondRow = 320;
        config.Conveyor.Rfid = 90;
        config.Conveyor.Storage = 460;
        config.Conveyor.PalletOffset = 70;

        config.Conveyor.MoveVelocity = 250;
        config.Conveyor.MoveAccel = 1200;
        config.Conveyor.MoveDecel = 1200;
        config.Conveyor.MoveJerk = 12000;
        config.Conveyor.JogVelocity = 60;
        config.Conveyor.JogAccel = 400;
        config.Conveyor.JogDecel = 400;
        config.Conveyor.JogJerk = 4000;
        config.Conveyor.StopDecel = 1800;

        config.Conveyor.SlowDownFactor = 0.35;
        config.Conveyor.SlowDownMargin = 25;
        config.Conveyor.PositionTolerance = 1.5;

        config.SlotOrder[0] = PalletSlot.FirstRowRight;
        config.SlotOrder[1] = PalletSlot.FirstRowLeft;
        config.SlotOrder[2] = PalletSlot.SecondRowLeft;
        config.SlotOrder[3] = PalletSlot.SecondRowRight;

        return config;
    }

    private static void CopyConfig(PlcConfigSnapshot source, PlcConfigSnapshot target)
    {
        var a = source.Arm;
        var ta = target.Arm;
        ta.Home = a.Home; ta.PalletCenter = a.PalletCenter; ta.PalletLeft = a.PalletLeft;
        ta.PalletRight = a.PalletRight; ta.SlotLeft = a.SlotLeft; ta.SlotRight = a.SlotRight;
        ta.DropLeft = a.DropLeft; ta.DropRight = a.DropRight; ta.ColorSensor = a.ColorSensor;
        ta.TravelMin = a.TravelMin; ta.TravelMax = a.TravelMax; ta.JogMin = a.JogMin; ta.JogMax = a.JogMax;
        ta.MoveVelocity = a.MoveVelocity; ta.MoveAccel = a.MoveAccel; ta.MoveDecel = a.MoveDecel;
        ta.MoveJerk = a.MoveJerk; ta.JogVelocity = a.JogVelocity; ta.JogAccel = a.JogAccel;
        ta.JogDecel = a.JogDecel; ta.JogJerk = a.JogJerk; ta.StopDecel = a.StopDecel;
        ta.VacuumDetectionTimeout = a.VacuumDetectionTimeout; ta.ResetSettleTime = a.ResetSettleTime;
        ta.ResetStopTimeout = a.ResetStopTimeout; ta.KeepPoweredAfterMove = a.KeepPoweredAfterMove;

        var c = source.Conveyor;
        var tc = target.Conveyor;
        tc.FirstRow = c.FirstRow; tc.SecondRow = c.SecondRow; tc.Rfid = c.Rfid;
        tc.Storage = c.Storage; tc.PalletOffset = c.PalletOffset;
        tc.MoveVelocity = c.MoveVelocity; tc.MoveAccel = c.MoveAccel; tc.MoveDecel = c.MoveDecel;
        tc.MoveJerk = c.MoveJerk; tc.JogVelocity = c.JogVelocity; tc.JogAccel = c.JogAccel;
        tc.JogDecel = c.JogDecel; tc.JogJerk = c.JogJerk; tc.StopDecel = c.StopDecel;
        tc.SlowDownFactor = c.SlowDownFactor; tc.SlowDownMargin = c.SlowDownMargin;
        tc.PositionTolerance = c.PositionTolerance;

        Array.Copy(source.SlotOrder, target.SlotOrder, source.SlotOrder.Length);
    }
}
