namespace ZEM_BoschRexrothSystemByASTI.Plc;

/// <summary>The three pallet columns. 0 is the conveyor, not a separate structure.</summary>
public enum Region
{
    Left = -1,
    Center = 0,
    Right = 1
}

/// <summary>
/// What sits in a pallet slot or in the vacuum gripper.
/// <see cref="Unknown"/> means "the pallet is there but was never scanned",
/// <see cref="Missing"/> means "the slot is empty". They are not the same thing.
/// </summary>
public enum ObjectType
{
    Unknown = 0,
    Missing = 1,
    NoColor = 2,
    Red = 3,
    Green = 4,
    Cyan = 5,
    Gray = 6,
    Orange = 7,
    White = 8,
    Black = 9
}

/// <summary>
/// Slot layout on a pallet, as indexes into <c>_objectTypes</c>:
/// <code>
///    2 | 3   back row
///   ---+---
///    1 | 0   front row
/// </code>
/// </summary>
public enum PalletSlot
{
    Invalid = -1,
    FirstRowRight = 0,
    FirstRowLeft = 1,
    SecondRowLeft = 2,
    SecondRowRight = 3
}

/// <summary>Halt codes reported by <c>Diag</c>. Always negative, 0 means "no halt".</summary>
public enum HaltCode
{
    None = 0,
    NoPullers = -1,
    GripperShut = -2,
    InvalidArg = -10,
    PoolFull = -14,
    Timeout = -20,
    DriveError = -30
}

public static class PlcEnums
{
    public static readonly Region[] AllRegions = { Region.Left, Region.Center, Region.Right };

    /// <summary>PLC arrays are declared <c>ARRAY[-1..1]</c>; OPC UA delivers them zero based.</summary>
    public static int ToArrayIndex(this Region region) => (int)region + 1;

    public static Region ToRegion(int index) => (Region)index;

    public static string Label(this Region region) => region switch
    {
        Region.Left => "Stanga",
        Region.Center => "Banda",
        Region.Right => "Dreapta",
        _ => region.ToString()
    };

    public static string Label(this ObjectType type) => type switch
    {
        ObjectType.Unknown => "Nescanat",
        ObjectType.Missing => "Gol",
        ObjectType.NoColor => "Fara culoare",
        ObjectType.Red => "Rosu",
        ObjectType.Green => "Verde",
        ObjectType.Cyan => "Cyan",
        ObjectType.Gray => "Gri",
        ObjectType.Orange => "Portocaliu",
        ObjectType.White => "Alb",
        ObjectType.Black => "Negru",
        _ => $"?{(int)type}"
    };

    /// <summary>
    /// Fill colour for the drawing. These are the ARGB values the PLC itself uses in
    /// <c>HMI.ShowCurrentObjectColor</c>, so the two interfaces show the same colour for the same
    /// object. Unknown and Missing are drawn by the glyph itself.
    /// </summary>
    public static string Fill(this ObjectType type) => type switch
    {
        ObjectType.Red => "#FF0000",
        ObjectType.Green => "#00C000",
        ObjectType.Cyan => "#00ECFF",
        ObjectType.Gray => "#A9A9A9",
        ObjectType.Orange => "#FFA500",
        ObjectType.White => "#FFFFFF",
        ObjectType.Black => "#000000",
        // No colour is the gradient from `Unknown.svg`, the stand drawing for exactly this state.
        // Grey, as it was, was confused with the unscanned slot - both came out as faded squares.
        ObjectType.NoColor => "linear-gradient(135deg,#f93,#f0f)",
        ObjectType.Unknown => "#3b4250",
        _ => "#2a2f38"
    };

    /// <summary>
    /// The whole background declaration, not just the colour, and the two properties have to travel
    /// with it: `background` is a shorthand, so writing it in the inline style resets
    /// `background-origin` and `background-repeat` to their defaults — no matter that the class set
    /// them. And on an element with a border, the default means the gradient is laid out in the
    /// padding box, painted out to the border box and repeated: the 1px ring shows the neighbouring
    /// tile, which is exactly the colour from the other end of the gradient.
    /// </summary>
    public static string FillStyle(this ObjectType type) =>
        $"background:{type.Fill()};background-origin:border-box;background-repeat:no-repeat";

    /// <summary>True when the slot actually holds something worth drawing as an object.</summary>
    public static bool IsPresent(this ObjectType type) =>
        type != ObjectType.Missing;

    public static string Label(this PalletSlot slot) => slot switch
    {
        PalletSlot.FirstRowRight => "Fata dreapta",
        PalletSlot.FirstRowLeft => "Fata stanga",
        PalletSlot.SecondRowLeft => "Spate stanga",
        PalletSlot.SecondRowRight => "Spate dreapta",
        _ => "Invalid"
    };

    public static string Label(this HaltCode code) => code switch
    {
        HaltCode.None => "fara oprire",
        HaltCode.NoPullers => "pullerele nu sunt scoase",
        HaltCode.GripperShut => "gripper inchis",
        HaltCode.InvalidArg => "argument invalid",
        HaltCode.PoolFull => "pool plin",
        HaltCode.Timeout => "timeout",
        HaltCode.DriveError => "eroare de drive",
        _ => $"cod necunoscut {(int)code}"
    };

    public static string Name(this HaltCode code) => code switch
    {
        HaltCode.None => "H_NONE",
        HaltCode.NoPullers => "H_NO_PULLERS",
        HaltCode.GripperShut => "H_GRIPPER_SHUT",
        HaltCode.InvalidArg => "H_INVALID_ARG",
        HaltCode.PoolFull => "H_POOL_FULL",
        HaltCode.Timeout => "H_TIMEOUT",
        HaltCode.DriveError => "H_DRIVE_ERROR",
        _ => ((int)code).ToString()
    };
}
