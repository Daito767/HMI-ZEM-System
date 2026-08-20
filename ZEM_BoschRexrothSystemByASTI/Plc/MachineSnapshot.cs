namespace ZEM_BoschRexrothSystemByASTI.Plc;

/// <summary>
/// One axis, as the HMI is allowed to see it. The five error bits are shown as a single lamp:
/// the operator needs to know the axis is faulted, the detail is in Diag.
/// </summary>
public sealed class AxisState
{
    public double Position { get; set; }
    public bool PowerOn { get; set; }
    public bool Busy { get; set; }

    public bool StepError { get; set; }
    public bool JogError { get; set; }
    public bool MoveAbsoluteError { get; set; }
    public bool InterruptError { get; set; }
    public bool ContinueError { get; set; }

    public bool HasError =>
        StepError || JogError || MoveAbsoluteError || InterruptError || ContinueError;

    /// <summary>The axis is busy or jogging, so the manual inputs are locked out by the PLC.</summary>
    public bool InputsDisabled { get; set; }
}

/// <summary>The process image, one flag per sensor and per valve command.</summary>
public sealed class IoState
{
    public bool ArmExtended { get; set; }
    public bool ArmRetracted { get; set; }
    public bool ArmExtendCmd { get; set; }
    public bool ArmRetractCmd { get; set; }

    public bool GripperClosed { get; set; }
    public bool GripperCloseCmd { get; set; }
    public bool VacuumDetected { get; set; }
    public bool VacuumCmd { get; set; }

    public bool PullerLeftExtended { get; set; }
    public bool PullerRightExtended { get; set; }
    public bool PullerLeftRetracted { get; set; }
    public bool PullerRightRetracted { get; set; }
    public bool PullerExtendCmd { get; set; }
    public bool PullerRetractCmd { get; set; }

    public bool GateTopForwardRetracted { get; set; }
    public bool GateTopBackwardRetracted { get; set; }
    public bool GateBottomLeftRetracted { get; set; }
    public bool GateBottomRightRetracted { get; set; }
    public bool GatesTopRetractCmd { get; set; }
    public bool GatesBottomRetractCmd { get; set; }

    public bool ExistForwardNear { get; set; }
    public bool ExistForwardFar { get; set; }
    public bool ExistBackwardNear { get; set; }
    public bool ExistBackwardFar { get; set; }

    public bool AirPressureOk { get; set; }
    public bool StorageNotEmpty { get; set; }
    public bool ButtonStart { get; set; }

    /// <summary>Composed the way FB_ConveyorController.GetCenterDistance does it.</summary>
    public int DistanceCenter1 { get; set; }
    public int DistanceCenter2 { get; set; }
    public int ConveyorRawDistance => DistanceCenter1 * 256 + DistanceCenter2;

    /// <summary>The colour sensor's seven outputs, in the order of <see cref="ColorSensorTypes"/>.</summary>
    public bool[] ColorSensors { get; } = new bool[7];

    public static readonly ObjectType[] ColorSensorTypes =
    {
        ObjectType.Red, ObjectType.Green, ObjectType.Cyan, ObjectType.Gray,
        ObjectType.Orange, ObjectType.White, ObjectType.Black
    };

    public int[] AnalogIn { get; } = new int[4];

    /// <summary>The raw word the PLC put on each output.</summary>
    public int[] AnalogOut { get; } = new int[2];

    public bool RfidPresent { get; set; }
    public bool RfidExistTag { get; set; }
    public bool RfidReady { get; set; }
    public bool RfidError { get; set; }
    public bool RfidAlarm1 { get; set; }
    public bool RfidAlarm2 { get; set; }
    public bool RfidAntennaEnabled { get; set; }
    public int RfidStatusByte { get; set; }
    public int RfidSignalLevel { get; set; }

    public int[] RfidReadBytes { get; } = new int[8];
    public int[] RfidWriteBytes { get; } = new int[8];
}

/// <summary>What the <c>HMI</c> program reports back about the commands it is running.</summary>
public sealed class HmiState
{
    /// <summary>Distance of the conveyor, already computed by the PLC.</summary>
    public double ConveyorDistance { get; set; }

    /// <summary>ARGB, ready to paint. The PLC picks it from its own palette.</summary>
    public uint CurrentObjectColor { get; set; }

    /// <summary>One counter per colour, from NoColor(2) to Black(9).</summary>
    public int[] ColorCounts { get; } = new int[8];

    /// <summary>The setpoint, in millivolts, that the PLC will convert on the next write.</summary>
    public double[] AnalogOutValue { get; } = new double[2];

    /// <summary>The requested relative move fits inside the travel limits.</summary>
    public bool AllowRelativeMovement { get; set; }

    public bool WaitForMoveAbsolute { get; set; }
    public bool WaitForMoveRelative { get; set; }

    public int CountOf(ObjectType type)
    {
        var index = (int)type - (int)ObjectType.NoColor;
        return index >= 0 && index < ColorCounts.Length ? ColorCounts[index] : 0;
    }

    /// <summary>CSS colour from the PLC's ARGB word; null when nothing has been scanned.</summary>
    public string? CurrentColorCss()
    {
        if (CurrentObjectColor == 0) return null;
        var r = (CurrentObjectColor >> 16) & 0xFF;
        var g = (CurrentObjectColor >> 8) & 0xFF;
        var b = CurrentObjectColor & 0xFF;
        return $"rgb({r},{g},{b})";
    }
}

/// <summary>The two sorting tables from <c>Main</c>.</summary>
public sealed class PolicyState
{
    public const int ObjectPolicyCount = 10;
    public const int PalletPolicyCount = 6;

    /// <summary>0 leave on the pallet, 1 drop left, 2 drop right. Indexed by ENUM_ObjectType.</summary>
    public int[] DropObject { get; } = new int[ObjectPolicyCount];

    /// <summary>0 left, anything else right. Indexed by the pallet number minus one.</summary>
    public int[] DropPallet { get; } = new int[PalletPolicyCount];

    public DateTime? LoadedAt { get; set; }

    public static string ObjectPolicyLabel(int value) => value switch
    {
        0 => "Lasa pe paleta",
        1 => "Arunca stanga",
        2 => "Arunca dreapta",
        _ => $"necunoscut ({value})"
    };

    public static string PalletPolicyLabel(int value) => value == 0 ? "Stanga" : "Dreapta";
}

/// <summary>Everything outside the pallet model: the axes, the process image, the command layer.</summary>
public sealed class MachineSnapshot
{
    public AxisState Arm { get; } = new();
    public AxisState Conveyor { get; } = new();
    public IoState Io { get; } = new();
    public HmiState Hmi { get; } = new();

    public DateTime UpdatedAt { get; set; }
}
