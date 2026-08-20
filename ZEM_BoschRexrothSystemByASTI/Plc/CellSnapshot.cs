namespace ZEM_BoschRexrothSystemByASTI.Plc;

/// <summary>One entry of <c>Layout.Pool</c>: the identity and the content of a pallet.</summary>
public sealed class PalletInfo
{
    public const int SlotCount = 4;

    public bool IsValid { get; set; }

    /// <summary>Index in the pool. This is the identity, not <see cref="RealId"/>.</summary>
    public int VirtualId { get; set; } = -1;

    /// <summary>Id read by RFID, 0 when never read. Not a key: pallets may share it or have none.</summary>
    public int RealId { get; set; }

    public ObjectType[] Slots { get; } = Enumerable.Repeat(ObjectType.Missing, SlotCount).ToArray();

    /// <summary>The same slots as text, straight from the PLC. Only refreshed on demand — it is heavy.</summary>
    public string[] SlotNames { get; } = new string[SlotCount];

    public int ObjectCount => Slots.Count(s => s != ObjectType.Missing);

    public PalletInfo Clone()
    {
        var copy = new PalletInfo { IsValid = IsValid, VirtualId = VirtualId, RealId = RealId };
        Array.Copy(Slots, copy.Slots, SlotCount);
        Array.Copy(SlotNames, copy.SlotNames, SlotCount);
        return copy;
    }
}

/// <summary>One column of the cell. Index 0 of <see cref="PalletIds"/> is at the front, where the arm reaches.</summary>
public sealed class RowState
{
    public const int MaxCapacity = 6;

    public Region Region { get; init; }
    public int Count { get; set; }
    public int Capacity { get; set; } = MaxCapacity;
    public int[] PalletIds { get; } = Enumerable.Repeat(-1, MaxCapacity).ToArray();

    /// <summary>The front pallet of a side column is out, held by the pullers. Meaningless for the conveyor.</summary>
    public bool IsAtFront { get; set; }

    public int DroppedCount { get; set; }

    /// <summary>Only the first <see cref="Count"/> entries are valid; the rest are -1.</summary>
    public IEnumerable<int> ValidPalletIds => PalletIds.Take(Math.Clamp(Count, 0, MaxCapacity));

    public RowState Clone()
    {
        var copy = new RowState
        {
            Region = Region, Count = Count, Capacity = Capacity,
            IsAtFront = IsAtFront, DroppedCount = DroppedCount
        };
        Array.Copy(PalletIds, copy.PalletIds, MaxCapacity);
        return copy;
    }
}

/// <summary>Everything the HMI shows about the cell, as of one refresh cycle.</summary>
public sealed class CellSnapshot
{
    public const int PoolSize = 6;

    // Main - sequence state
    public bool Run { get; set; }
    public bool ResetStarted { get; set; }
    public int MainStep { get; set; }

    // Main.Layout
    public Dictionary<Region, RowState> Rows { get; } = PlcEnums.AllRegions
        .ToDictionary(r => r, r => new RowState { Region = r });

    public PalletInfo[] Pool { get; } = Enumerable.Range(0, PoolSize)
        .Select(i => new PalletInfo { VirtualId = i }).ToArray();

    /// <summary>VirtualId held by the gripper, -1 when empty. That pallet is already out of its queue.</summary>
    public int InGripper { get; set; } = -1;

    public ObjectType InVacuum { get; set; } = ObjectType.Missing;

    /// <summary>How many pool entries are in use.</summary>
    public int PalletCount { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>True when the colour strings were refreshed at least once (they are polled on demand).</summary>
    public bool HasColorNames { get; set; }

    public bool GripperHoldsPallet => InGripper >= 0 && InGripper < PoolSize;
    public bool VacuumHoldsObject => InVacuum != ObjectType.Missing;

    public PalletInfo? PalletById(int virtualId) =>
        virtualId >= 0 && virtualId < PoolSize ? Pool[virtualId] : null;

    /// <summary>Where a pool entry currently sits. Null when it is not placed anywhere.</summary>
    public string? LocationOf(int virtualId)
    {
        if (InGripper == virtualId) return "In gripper";
        foreach (var region in PlcEnums.AllRegions)
        {
            var row = Rows[region];
            var index = Array.IndexOf(row.PalletIds, virtualId);
            if (index >= 0 && index < row.Count)
                return index == 0 ? $"{region.Label()}, in fata" : $"{region.Label()}, pozitia {index}";
        }
        return null;
    }

    public CellSnapshot Clone()
    {
        var copy = new CellSnapshot
        {
            Run = Run, ResetStarted = ResetStarted, MainStep = MainStep,
            InGripper = InGripper, InVacuum = InVacuum, PalletCount = PalletCount,
            UpdatedAt = UpdatedAt, HasColorNames = HasColorNames
        };
        foreach (var region in PlcEnums.AllRegions)
            copy.Rows[region] = Rows[region].Clone();
        for (var i = 0; i < PoolSize; i++)
            copy.Pool[i] = Pool[i].Clone();
        return copy;
    }
}

/// <summary>One line of the <c>Diag</c> ring buffer.</summary>
public sealed class DiagEntry
{
    public string Source { get; set; } = string.Empty;
    public int Step { get; set; }
    public HaltCode Code { get; set; }
    public uint Cycle { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Source) && Cycle == 0;
}

public sealed class DiagSnapshot
{
    public const int HistorySize = 32;

    /// <summary>There is an unacknowledged halt.</summary>
    public bool Active { get; set; }

    /// <summary>Valid entries in the history, at most 32.</summary>
    public int Count { get; set; }

    /// <summary>Next write position in the ring buffer.</summary>
    public int Head { get; set; }

    /// <summary>MainTask cycle counter.</summary>
    public uint Cycle { get; set; }

    public DiagEntry Last { get; set; } = new();

    /// <summary>Raw ring buffer, only loaded when the diagnostics panel asks for it.</summary>
    public DiagEntry[] History { get; set; } = Array.Empty<DiagEntry>();

    public DateTime? HistoryLoadedAt { get; set; }

    /// <summary>The ring unrolled newest first: <c>(Head - 1) MOD 32</c> going back <see cref="Count"/> entries.</summary>
    public IEnumerable<DiagEntry> HistoryNewestFirst()
    {
        if (History.Length == 0) yield break;
        var size = History.Length;
        var valid = Math.Clamp(Count, 0, size);
        for (var i = 1; i <= valid; i++)
        {
            var index = ((Head - i) % size + size) % size;
            yield return History[index];
        }
    }
}

public sealed class ArmConfig
{
    public double Home { get; set; }
    public double PalletCenter { get; set; }
    public double PalletLeft { get; set; }
    public double PalletRight { get; set; }
    public double SlotLeft { get; set; }
    public double SlotRight { get; set; }
    public double DropLeft { get; set; }
    public double DropRight { get; set; }
    public double ColorSensor { get; set; }
    public double TravelMin { get; set; }
    public double TravelMax { get; set; }
    public double JogMin { get; set; }
    public double JogMax { get; set; }

    public double MoveVelocity { get; set; }
    public double MoveAccel { get; set; }
    public double MoveDecel { get; set; }
    public double MoveJerk { get; set; }
    public double JogVelocity { get; set; }
    public double JogAccel { get; set; }
    public double JogDecel { get; set; }
    public double JogJerk { get; set; }
    public double StopDecel { get; set; }

    public TimeSpan VacuumDetectionTimeout { get; set; }
    public TimeSpan ResetSettleTime { get; set; }
    public TimeSpan ResetStopTimeout { get; set; }
    public bool KeepPoweredAfterMove { get; set; }

    /// <summary>Named positions along the rail, in the order they are drawn.</summary>
    public IEnumerable<(string Name, double Position)> NamedPositions()
    {
        yield return ("Home", Home);
        yield return ("PalletLeft", PalletLeft);
        yield return ("PalletCenter", PalletCenter);
        yield return ("PalletRight", PalletRight);
        yield return ("SlotLeft", SlotLeft);
        yield return ("SlotRight", SlotRight);
        yield return ("DropLeft", DropLeft);
        yield return ("DropRight", DropRight);
        yield return ("ColorSensor", ColorSensor);
    }
}

public sealed class ConveyorConfig
{
    public double FirstRow { get; set; }
    public double SecondRow { get; set; }
    public double Rfid { get; set; }
    public double Storage { get; set; }
    public double PalletOffset { get; set; }

    public double MoveVelocity { get; set; }
    public double MoveAccel { get; set; }
    public double MoveDecel { get; set; }
    public double MoveJerk { get; set; }
    public double JogVelocity { get; set; }
    public double JogAccel { get; set; }
    public double JogDecel { get; set; }
    public double JogJerk { get; set; }
    public double StopDecel { get; set; }

    public double SlowDownFactor { get; set; }
    public double SlowDownMargin { get; set; }
    public double PositionTolerance { get; set; }
}

/// <summary>
/// <c>GVL_Config</c>. Read-only over OPC UA today; see OPCUA-HMI.md §8 for what it would take
/// to re-teach positions from the HMI.
/// </summary>
public sealed class PlcConfigSnapshot
{
    public ArmConfig Arm { get; } = new();
    public ConveyorConfig Conveyor { get; } = new();
    public PalletSlot[] SlotOrder { get; } = Enumerable.Repeat(PalletSlot.Invalid, 4).ToArray();
    public DateTime? LoadedAt { get; set; }
}
