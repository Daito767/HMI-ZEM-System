namespace ZEM_BoschRexrothSystemByASTI.Plc;

/// <summary>
/// The variables the HMI binds to, as logical dotted names. Not the same count as what the PLC
/// publishes, and never worth writing down here: an array the PLC declares once is named here
/// once per element, so the two numbers count different things. Array indexes are written the way
/// the PLC declares them (<c>Rows[-1]</c>, <c>Pool[0]</c>); the symbol table maps them onto
/// whatever the server actually exposes — one node per element, or one array node read as a whole.
///
/// Source: OPCUA-HMI.md next to the project. If the symbol configuration changes, that file wins.
/// </summary>
public static class PlcSymbols
{
    public const string MainRoot = "Main";
    public const string HmiRoot = "HMI";
    public const string IoRoot = "IOs";
    public const string DiagRoot = "GVL_Diag";
    public const string ConfigRoot = "GVL_Config";

    /// <summary>The names the browse looks for to recognise the application node.</summary>
    public static readonly string[] Roots = { MainRoot, HmiRoot, IoRoot, DiagRoot, ConfigRoot };

    // --- Main: commands (the only writable nodes) and sequence state -----------------

    public const string StartCommand = "Main.StartCommand";
    public const string ResetCommand = "Main.ResetCommand";
    public const string PauseCommand = "Main.PauseCommand";
    public const string EndStepPauseCommand = "Main.EndStepPauseCommand";
    public const string Run = "Main.Run";
    public const string ResetStarted = "Main.ResetStarted";
    public const string MainStep = "Main.MainStep";

    // --- Main: sorting policy (the only other writable things in Main) -----------------

    /// <summary>0 leave on the pallet, 1 drop left, 2 drop right. Indexed by ENUM_ObjectType.</summary>
    public static string DropObjectPolicy(int objectType) => $"Main.DropObjectPolicyTable[{objectType}]";

    /// <summary>0 left, anything else right. Indexed by the pallet number minus one.</summary>
    public static string DropPalletPolicy(int index) => $"Main.DropPalletePolicyTable[{index}]";

    // --- Main.<axis>Controller: the few fields the HMI is allowed to read ---------------

    public const string ArmController = "Main.ArmController";
    public const string ConveyorController = "Main.ConveyorController";
    public const string RfidController = "Main.RFID_Controller";

    public static string AxisPosition(string controller) => $"{controller}.ReadPosition.Position";
    public static string AxisPowerStatus(string controller) => $"{controller}.Power.Status";
    public static string AxisBusy(string controller) => $"{controller}.Busy";
    public static string AxisStepError(string controller) => $"{controller}.StepError";
    public static string AxisJogError(string controller) => $"{controller}.Jog.Error";
    public static string AxisMoveAbsoluteError(string controller) => $"{controller}.MoveAbsolute.Error";
    public static string AxisInterruptError(string controller) => $"{controller}.AxisInterrupt.Error";
    public static string AxisContinueError(string controller) => $"{controller}.AxisContinue.Error";

    // --- HMI: the command layer. Everything manual goes through here --------------------

    public const string ArmJogLeft = "HMI.ArmJogLeft";
    public const string ArmJogRight = "HMI.ArmJogRight";
    public const string ConveyorJogForward = "HMI.ConveyorJogForward";
    public const string ConveyorJogBackward = "HMI.ConveyorJogBackward";

    public const string ArmSetPower = "HMI.ArmSetPower";
    public const string ConveyorSetPower = "HMI.ConveyorSetPower";
    public const string ArmStop = "HMI.ArmStop";
    public const string ConveyorStop = "HMI.ConveyorStop";
    public const string ArmReset = "HMI.ArmReset";
    public const string ConveyorReset = "HMI.ConveyorReset";

    public const string ArmMoveAbsolute = "HMI.ArmMoveAbsolute";
    public const string ArmMoveAbsolutePosition = "HMI.ArmMoveAbsolutePosition";
    public const string ArmMoveRelative = "HMI.ArmMoveRelative";
    public const string ArmMoveRelativePosition = "HMI.ArmMoveRelativePosition";

    public const string ArmDeactivateInputs = "HMI.ArmDeactivateInputs";
    public const string ConveyorDeactivateInputs = "HMI.ConveyorDeactivateInputs";
    public const string ArmAllowRelativeMovement = "HMI.ArmAllowRelativeMovement";
    public const string ArmWaitForMoveAbsolute = "HMI.ArmWaitForMoveAbsolute";
    public const string ArmWaitForMoveRelative = "HMI.ArmWaitForMoveRelative";

    public const string RfidRead = "HMI.RfidRead";
    public const string RfidWrite = "HMI.RfidWrite";

    public const string ConveyorDistance = "HMI.ConveyorDistance";
    public const string CurrentObjectColor = "HMI.CurrentObjectColor";

    /// <summary>
    /// The value in millivolts the operator asks for. The PLC turns it into the raw word with
    /// <c>IOs.AO := REAL_TO_INT(Value * 1.03489 + 39.33)</c> as long as the write flag is up - which
    /// is why the flag has to be pulsed, not left standing.
    /// </summary>
    public static string ValueAnalogOut(int index) => $"HMI.ValueAO{index}";

    public static string WriteAnalogOut(int index) => $"HMI.WriteAO{index}";

    /// <summary>One counter per colour, indexed by ENUM_ObjectType from NoColor(2) to Black(9).</summary>
    public static string ColorCount(int objectType) => $"HMI.ColorCount[{objectType}]";

    /// <summary>
    /// Every level flag the HMI can raise. This is also the list that gets written FALSE when the
    /// connection is going away: there is no watchdog in the PLC, and a jog flag left TRUE means the
    /// axis keeps going until it reaches its travel limit.
    /// </summary>
    public static readonly string[] CommandFlags =
    {
        ArmJogLeft, ArmJogRight, ConveyorJogForward, ConveyorJogBackward,
        ArmSetPower, ConveyorSetPower, ArmStop, ConveyorStop,
        ArmMoveAbsolute, ArmMoveRelative, RfidRead, RfidWrite,
        "HMI.WriteAO1", "HMI.WriteAO2"
    };

    // --- IOs: the process image ---------------------------------------------------------

    public const string ArmExtended = "IOs.Arm_Extended";
    public const string ArmRetracted = "IOs.Arm_Retracted";
    public const string ArmExtendCmd = "IOs.Arm_Extend_Cmd";
    public const string ArmRetractCmd = "IOs.Arm_Retract_Cmd";

    public const string GripperClosed = "IOs.Gripper_Closed";
    public const string GripperCloseCmd = "IOs.Gripper_Close_Cmd";
    public const string VacuumDetected = "IOs.Vacuum_Detected";
    public const string VacuumCmd = "IOs.Vacuum_Cmd";

    public const string PullerLeftExtended = "IOs.Puller_Left_Extended";
    public const string PullerRightExtended = "IOs.Puller_Right_Extended";
    public const string PullerLeftRetracted = "IOs.Puller_Left_Retracted";
    public const string PullerRightRetracted = "IOs.Puller_Right_Retracted";
    public const string PullerExtendCmd = "IOs.Puller_Extend_Cmd";
    public const string PullerRetractCmd = "IOs.Puller_Retract_Cmd";

    public const string GateTopForwardRetracted = "IOs.Storage_Gate_Top_Forward_Retracted";
    public const string GateTopBackwardRetracted = "IOs.Storage_Gate_Top_Backward_Retracted";
    public const string GateBottomLeftRetracted = "IOs.Storage_Gate_Bottom_Left_Retracted";
    public const string GateBottomRightRetracted = "IOs.Storage_Gate_Bottom_Right_Retracted";
    public const string GatesTopRetractCmd = "IOs.Storage_Gates_Top_Retract_Cmd";
    public const string GatesBottomRetractCmd = "IOs.Storage_Gates_Bottom_Retract_Cmd";

    public const string ExistForwardNear = "IOs.Exist_Forward_Near";
    public const string ExistForwardFar = "IOs.Exist_Forward_Far";

    /// <summary>Spelled this way in the project.</summary>
    public const string ExistBackwardNear = "IOs.Exist_Bacward_Near";

    public const string ExistBackwardFar = "IOs.Exist_Backward_Far";

    public const string AirPressureOk = "IOs.Air_Presure_Ok";
    public const string StorageNotEmpty = "IOs.Storage_Not_Empty";
    public const string ButtonStart = "IOs.Button_Start";

    public const string DistanceCenter1 = "IOs.Distance_Center_1";
    public const string DistanceCenter2 = "IOs.Distance_Center_2";

    public static readonly string[] ColorSensors =
    {
        "IOs.Red", "IOs.Green", "IOs.Cyan", "IOs.Gray", "IOs.Orange", "IOs.White", "IOs.Black"
    };

    public static string AnalogIn(int index) => $"IOs.AI{index}";

    /// <summary>The raw word the PLC put on the output, after its own conversion.</summary>
    public static string AnalogOut(int index) => $"IOs.AO{index}";

    public const string RfidPresent = "IOs.RFID_Present";
    public const string RfidExistTag = "IOs.RFID_Exist_Tag";
    public const string RfidReadyFlag = "IOs.RFID_Ready_Flag";
    public const string RfidError = "IOs.RFID_Error";
    public const string RfidAlarm1 = "IOs.RFID_Alarm_1";
    public const string RfidAlarm2 = "IOs.RFID_Alarm_2";
    public const string RfidAntennaEnabled = "IOs.RFID_Antenna_Enabled";
    public const string RfidStatusByte = "IOs.RFID_Status_Byte";
    public const string RfidSignalLevel = "IOs.RFID_Signal_Level";

    public static string RfidReadByte(int index) => $"IOs.RFID_Read_Byte_{index}";
    public static string RfidWriteByte(int index) => $"IOs.RFID_Write_Byte_{index}";

    /// <summary>The valve commands the pneumatic page may drive, and only while the cell is stopped.</summary>
    public static readonly string[] ValveCommands =
    {
        ArmExtendCmd, ArmRetractCmd, GripperCloseCmd, VacuumCmd,
        PullerExtendCmd, PullerRetractCmd, GatesTopRetractCmd, GatesBottomRetractCmd
    };

    // --- Main.Layout: the state of the cell ------------------------------------------

    public const string LayoutRoot = "Main.Layout";
    public const string InGripper = "Main.Layout.InGripper";
    public const string InVacuum = "Main.Layout.InVacuum";
    public const string PalletCount = "Main.Layout.PalletCount";

    public static string RowCount(Region r) => $"Main.Layout.Rows[{(int)r}]._count";
    public static string RowCapacity(Region r) => $"Main.Layout.Rows[{(int)r}]._capacity";
    public static string RowPalletId(Region r, int slot) => $"Main.Layout.Rows[{(int)r}]._pallets_id[{slot}]";
    public static string IsAtFront(Region r) => $"Main.Layout.IsAtFront[{(int)r}]";
    public static string DroppedCount(Region r) => $"Main.Layout.DroppedCount[{(int)r}]";

    public static string PoolIsValid(int p) => $"Main.Layout.Pool[{p}]._isValid";
    public static string PoolVirtualId(int p) => $"Main.Layout.Pool[{p}]._virtualId";
    public static string PoolRealId(int p) => $"Main.Layout.Pool[{p}]._realId";
    public static string PoolObjectType(int p, int slot) => $"Main.Layout.Pool[{p}]._objectTypes[{slot}]";
    public static string PoolColorName(int p, int slot) => $"Main.Layout.Pool[{p}]._objectColorsStr[{slot}]";

    // --- GVL_Diag ---------------------------------------------------------------------

    public const string DiagActive = "GVL_Diag.Diag.Active";
    public const string DiagCount = "GVL_Diag.Diag.Count";
    public const string DiagHead = "GVL_Diag.Diag.Head";
    public const string DiagCycle = "GVL_Diag.Diag.Cycle";
    public const string DiagLastSource = "GVL_Diag.Diag.Last.Source";
    public const string DiagLastStep = "GVL_Diag.Diag.Last.Step";
    public const string DiagLastCode = "GVL_Diag.Diag.Last.Code";
    public const string DiagLastCycle = "GVL_Diag.Diag.Last.Cycle";

    public static string DiagHistorySource(int i) => $"GVL_Diag.Diag.History[{i}].Source";
    public static string DiagHistoryStep(int i) => $"GVL_Diag.Diag.History[{i}].Step";
    public static string DiagHistoryCode(int i) => $"GVL_Diag.Diag.History[{i}].Code";
    public static string DiagHistoryCycle(int i) => $"GVL_Diag.Diag.History[{i}].Cycle";

    // --- GVL_Config -------------------------------------------------------------------

    public static readonly string[] ArmPositions =
    {
        "Home", "PalletCenter", "PalletLeft", "PalletRight", "SlotLeft", "SlotRight",
        "DropLeft", "DropRight", "ColorSensor", "TravelMin", "TravelMax", "JogMin", "JogMax"
    };

    public static readonly string[] MotionFields =
    {
        "MoveVelocity", "MoveAccel", "MoveDecel", "MoveJerk",
        "JogVelocity", "JogAccel", "JogDecel", "JogJerk", "StopDecel"
    };

    public static readonly string[] ArmTimes =
    {
        "VacuumDetectionTimeout", "ResetSettleTime", "ResetStopTimeout"
    };

    public static readonly string[] ConveyorDistances =
    {
        "FirstRow", "SecondRow", "RFID", "Storage", "PalletOffset"
    };

    public static readonly string[] ConveyorScalars =
    {
        "SlowDownFactor", "SlowDownMargin", "PositionTolerance"
    };

    public static string ArmPos(string name) => $"GVL_Config.Arm.Pos.{name}";
    public static string ArmMotion(string name) => $"GVL_Config.Arm.Motion.{name}";
    public static string ArmField(string name) => $"GVL_Config.Arm.{name}";
    public const string ArmKeepPowered = "GVL_Config.Arm.KeepPoweredAfterMove";

    public static string ConveyorDist(string name) => $"GVL_Config.Conveyor.Dist.{name}";
    public static string ConveyorMotion(string name) => $"GVL_Config.Conveyor.Motion.{name}";
    public static string ConveyorField(string name) => $"GVL_Config.Conveyor.{name}";

    public static string SlotOrder(int i) => $"GVL_Config.Main.SlotOrder[{i}]";

    // --- Groups used by the polling loop -----------------------------------------------

    /// <summary>Everything the refresh loop reads for the cell picture (~260 bytes worth of nodes).</summary>
    public static IEnumerable<string> CellLoop()
    {
        yield return Run;
        yield return ResetStarted;
        yield return MainStep;
        yield return InGripper;
        yield return InVacuum;
        yield return PalletCount;

        foreach (var r in PlcEnums.AllRegions)
        {
            yield return RowCount(r);
            yield return RowCapacity(r);
            yield return IsAtFront(r);
            yield return DroppedCount(r);
            for (var i = 0; i < RowState.MaxCapacity; i++)
                yield return RowPalletId(r, i);
        }

        for (var p = 0; p < CellSnapshot.PoolSize; p++)
        {
            yield return PoolIsValid(p);
            yield return PoolVirtualId(p);
            yield return PoolRealId(p);
            for (var s = 0; s < PalletInfo.SlotCount; s++)
                yield return PoolObjectType(p, s);
        }
    }

    /// <summary>The axes, the command layer's own state, and the process image.</summary>
    public static IEnumerable<string> MachineLoop()
    {
        foreach (var controller in new[] { ArmController, ConveyorController })
        {
            yield return AxisPowerStatus(controller);
            yield return AxisBusy(controller);
            yield return AxisStepError(controller);
            yield return AxisJogError(controller);
            yield return AxisInterruptError(controller);
            yield return AxisContinueError(controller);
        }

        // Only the arm is an axis in the full sense. The conveyor has no MoveAbsolute block, and no
        // axis position either - the belt has no encoder, its distance comes from the sensors
        // through GetCenterDistance(). Asking for a position it does not publish would leave one
        // symbol permanently unbound, and an unbound symbol is a warning that should mean something.
        yield return AxisPosition(ArmController);
        yield return AxisMoveAbsoluteError(ArmController);

        yield return ArmDeactivateInputs;
        yield return ConveyorDeactivateInputs;
        yield return ArmAllowRelativeMovement;
        yield return ArmWaitForMoveAbsolute;
        yield return ArmWaitForMoveRelative;
        yield return ConveyorDistance;
        yield return CurrentObjectColor;

        for (var i = 1; i <= 2; i++)
            yield return ValueAnalogOut(i);

        for (var type = (int)ObjectType.NoColor; type <= (int)ObjectType.Black; type++)
            yield return ColorCount(type);

        foreach (var symbol in IoLoop())
            yield return symbol;
    }

    public static IEnumerable<string> IoLoop()
    {
        yield return ArmExtended;
        yield return ArmRetracted;
        yield return ArmExtendCmd;
        yield return ArmRetractCmd;

        yield return GripperClosed;
        yield return GripperCloseCmd;
        yield return VacuumDetected;
        yield return VacuumCmd;

        yield return PullerLeftExtended;
        yield return PullerRightExtended;
        yield return PullerLeftRetracted;
        yield return PullerRightRetracted;
        yield return PullerExtendCmd;
        yield return PullerRetractCmd;

        yield return GateTopForwardRetracted;
        yield return GateTopBackwardRetracted;
        yield return GateBottomLeftRetracted;
        yield return GateBottomRightRetracted;
        yield return GatesTopRetractCmd;
        yield return GatesBottomRetractCmd;

        yield return ExistForwardNear;
        yield return ExistForwardFar;
        yield return ExistBackwardNear;
        yield return ExistBackwardFar;

        yield return AirPressureOk;
        yield return StorageNotEmpty;
        yield return ButtonStart;

        yield return DistanceCenter1;
        yield return DistanceCenter2;

        foreach (var sensor in ColorSensors)
            yield return sensor;

        for (var i = 1; i <= 4; i++)
            yield return AnalogIn(i);

        for (var i = 1; i <= 2; i++)
            yield return AnalogOut(i);

        yield return RfidPresent;
        yield return RfidExistTag;
        yield return RfidReadyFlag;
        yield return RfidError;
        yield return RfidAlarm1;
        yield return RfidAlarm2;
        yield return RfidAntennaEnabled;
        yield return RfidStatusByte;
        yield return RfidSignalLevel;

        for (var i = 0; i < 8; i++)
        {
            yield return RfidReadByte(i);
            yield return RfidWriteByte(i);
        }
    }

    /// <summary>The two sorting tables. They only change when someone edits them.</summary>
    public static IEnumerable<string> Policies()
    {
        for (var type = 0; type <= (int)ObjectType.Black; type++)
            yield return DropObjectPolicy(type);

        for (var i = 0; i < 6; i++)
            yield return DropPalletPolicy(i);
    }

    public static IEnumerable<string> DiagLoop()
    {
        yield return DiagActive;
        yield return DiagCount;
        yield return DiagHead;
        yield return DiagCycle;
        yield return DiagLastSource;
        yield return DiagLastStep;
        yield return DiagLastCode;
        yield return DiagLastCycle;
    }

    public static IEnumerable<string> DiagHistory()
    {
        for (var i = 0; i < DiagSnapshot.HistorySize; i++)
        {
            yield return DiagHistorySource(i);
            yield return DiagHistoryStep(i);
            yield return DiagHistoryCode(i);
            yield return DiagHistoryCycle(i);
        }
    }

    public static IEnumerable<string> ColorNames()
    {
        for (var p = 0; p < CellSnapshot.PoolSize; p++)
            for (var s = 0; s < PalletInfo.SlotCount; s++)
                yield return PoolColorName(p, s);
    }

    public static IEnumerable<string> Config()
    {
        foreach (var name in ArmPositions) yield return ArmPos(name);
        foreach (var name in MotionFields) yield return ArmMotion(name);
        foreach (var name in ArmTimes) yield return ArmField(name);
        yield return ArmKeepPowered;

        foreach (var name in ConveyorDistances) yield return ConveyorDist(name);
        foreach (var name in MotionFields) yield return ConveyorMotion(name);
        foreach (var name in ConveyorScalars) yield return ConveyorField(name);

        for (var i = 0; i < 4; i++) yield return SlotOrder(i);
    }

    /// <summary>Everything the HMI binds, for the connection report.</summary>
    public static IEnumerable<string> All() =>
        new[] { StartCommand, ResetCommand, PauseCommand, EndStepPauseCommand }
            .Concat(CommandFlags)
            .Concat(new[] { ArmMoveAbsolutePosition, ArmMoveRelativePosition })
            .Concat(new[] { ArmReset, ConveyorReset })
            .Concat(ValveCommands)
            .Concat(CellLoop())
            .Concat(MachineLoop())
            .Concat(Policies())
            .Concat(ColorNames())
            .Concat(DiagLoop())
            .Concat(DiagHistory())
            .Concat(Config());
}
