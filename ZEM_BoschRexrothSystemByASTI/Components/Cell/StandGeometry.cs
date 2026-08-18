using System.Globalization;
using ZEM_BoschRexrothSystemByASTI.Plc;

// MAUI publishes a Region of its own; without this alias the name is ambiguous in a plain .cs file.
using Region = ZEM_BoschRexrothSystemByASTI.Plc.Region;

namespace ZEM_BoschRexrothSystemByASTI.Components.Cell;

/// <summary>
/// Where every layer of the two stand drawings sits, in millimetres of the stand itself.
///
/// The drawings come from the old CODESYS visualisation. Each file is a full canvas with the part
/// already in place, so the layers are simply stacked and only ever moved as a whole - exactly what
/// the old animation did. The canvases are 750 x 900 mm seen from above and 750 x 500 mm seen from
/// the front, which is why offsets are written in millimetres here and turned into percentages of
/// the container at the last moment: a percentage needs no pixels-per-millimetre bus and no resize
/// listener, unlike the old <c>custom.tools</c>.
///
/// The numbers that carry over from the old animation are the ones it was tuned with. What is new
/// is the front view's depth: every drawing in that view shares one top edge, so the resting places
/// were set by hand in the CODESYS designer and are not recoverable from the files. Those constants
/// are grouped under "front depth" and are the ones to touch if the picture reads wrong.
/// </summary>
public static class StandGeometry
{
    public const double TopWidthMm = 750;
    public const double TopHeightMm = 900;
    public const double FrontWidthMm = 750;
    public const double FrontHeightMm = 500;

    // ---- top view, carried over from top_view.js / top_pallete_animation.js -------------------

    /// <summary>How far a puller travels when it goes out.</summary>
    public const double PullerReachMm = 124;

    /// <summary>A magazine gate opens by a nudge; the old animation used the same 10 mm.</summary>
    public const double GateTravelMm = 10;

    /// <summary>Belt distance at which the front pallet sits where the drawing puts it.</summary>
    public const double BeltBaselineMm = 560;

    /// <summary>
    /// Distance between two pallets queued one behind another. Only the fallback: the real one is
    /// <c>GVL_Config.Conveyor.Dist.PalletOffset</c>, read from the stand, and this 120 mm inherited
    /// from the old animation is what gets used until the configuration has been read.
    /// </summary>
    public const double BeltPitchMm = 120;

    /// <summary>Sideways offset from the belt to a side column.</summary>
    public const double ColumnShiftMm = 140;

    /// <summary>Axis position at which the arm is centred over the belt.</summary>
    public const double ArmBaselineMm = 266;

    /// <summary>Where a pallet held by the gripper sits, measured from the drawn resting place.</summary>
    public const double GripperRowMm = 490;

    /// <summary>
    /// How deep a pallet is in the drawing - 118 mm, the same as it is wide - and with that, the
    /// pitch inside a side column: there the puller pushes them against one another, so they touch.
    /// The belt is the other case, where the spacing is belt travel and comes from the PLC.
    /// </summary>
    public const double PalletDepthMm = 118;

    /// <summary>
    /// Front-most place inside a side column, with the puller drawn back. Not a number of its own:
    /// it is the place the arm picks from, less the stroke of the puller - which is exactly the
    /// move the puller makes. Checked against the drawing it comes out 4 mm from where the green
    /// pusher face actually sits, so the two agree.
    /// </summary>
    public const double ColumnFrontMm = GripperRowMm - PullerReachMm;

    // ---- front view, from front_view.js ------------------------------------------------------

    /// <summary>
    /// Vertical stroke of the single pneumatic head - gripper, claws and vacuum together.
    ///
    /// Stays at the 40 mm of the old animation. Lengthening it to reach into the pallet was tried
    /// and was wrong: the stroke is shared by the whole head, so the gripper only looked right at
    /// the bottom by plunging everything too deep everywhere else. The vacuum reaches its slot at
    /// this stroke, and the gripper goes down with it - see <see cref="CarriedLiftMm"/>.
    /// </summary>
    public const double HeadDropMm = 40;

    /// <summary>
    /// Sideways correction for the object held by the vacuum. The drawings are from the upgraded
    /// stand, where the vacuum was a head of its own, beside the gripper; here one piston carries
    /// everything, so the object came out left of the slot it belongs to.
    /// </summary>
    public const double VacuumPieceOffsetMm = 5;

    /// <summary>How far each claw moves inwards when the gripper closes.</summary>
    public const double ClawCloseMm = 3;

    /// <summary>
    /// Where the pallet in the gripper hangs, relative to where the drawing puts it. The only
    /// vertical move a pallet makes in the front view: everything standing on the stand sits on the
    /// belt rail, at the one height the drawings were made for.
    ///
    /// Not a number of its own but the stroke itself, and that is the whole point. A pallet does not
    /// move while it is being gripped, and once gripped it rises exactly as far as the head does -
    /// so at the bottom of the stroke this is zero, the pallet still standing where it stood, and at
    /// the top it is the full stroke. Written as a free constant it kept drifting away from
    /// <see cref="HeadDropMm"/>, and every millimetre of drift showed up as the gripper biting
    /// deeper into the pallet than it had descended.
    /// </summary>
    public static double CarriedLiftMm(double headYMm) => headYMm - HeadDropMm;

    // ---- helpers -----------------------------------------------------------------------------

    /// <summary>
    /// How far apart two pallets queued on the belt are drawn. The stand knows the number, so it is
    /// not ours to invent - and it belongs to the belt alone, where the spacing is how far the belt
    /// travelled between them. A side column is not that case: there the puller holds them against
    /// one another, so the pitch is the pallet itself. Zero means the configuration is not read yet.
    /// </summary>
    public static double PitchMm(PlcConfigSnapshot? config) =>
        config is { Conveyor.PalletOffset: > 1 } ? config.Conveyor.PalletOffset : BeltPitchMm;

    /// <summary>
    /// A pallet's place in a side column, counted from the front. The whole stack moves together
    /// when the puller has it pushed forward - a buffer holds its pallets against one another, so
    /// only the first one having advanced would leave a hole no pusher could make.
    /// </summary>
    public static double ColumnRowMm(int index, bool atFront) =>
        (atFront ? GripperRowMm : ColumnFrontMm) - index * PalletDepthMm;

    /// <summary>
    /// A pallet's place on the belt: the whole queue slides with the measured distance. Clamped,
    /// because a belt that reads nothing sends the queue off the canvas, and a pallet stuck against
    /// the edge is still a pallet somebody can see.
    /// </summary>
    public static double BeltRowMm(double distanceMm, int index, double pitchMm) =>
        Math.Clamp(BeltBaselineMm - (distanceMm + pitchMm * index), -190, 560);

    /// <summary>How dark a pallet gets at the far end of the belt. Not black - it is still a pallet.</summary>
    private const double ShadowFloor = 0.35;

    /// <summary>
    /// How far along the belt run a pallet has come: 0 where the drawing rests it, at the back of
    /// the run, and 1 once it has reached the place the arm picks from.
    /// </summary>
    public static double BeltPresence(double forwardMm) =>
        Math.Clamp(forwardMm / GripperRowMm, 0, 1);

    /// <summary>
    /// Seen from the front there is no depth to move a pallet through - perspective was tried and
    /// turned down - so the approach is told with light instead: a pallet at the far end of the belt
    /// stands in shadow and comes out of it as it advances towards the arm.
    ///
    /// Brightness and not opacity, because a pallet on the belt is drawn in two halves with the arm
    /// between them. Two half-transparent halves would show through one another along the seam;
    /// two dim ones still cover each other properly.
    /// </summary>
    public static string ShadeWithDepth(double forwardMm)
    {
        var light = ShadowFloor + (1 - ShadowFloor) * BeltPresence(forwardMm);

        return light > 0.995
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $";filter:brightness({light:0.###})");
    }

    /// <summary>Sideways offset of a column from the belt.</summary>
    public static double ColumnShift(Region region) => region switch
    {
        Region.Left => -ColumnShiftMm,
        Region.Right => ColumnShiftMm,
        _ => 0
    };

    /// <summary>
    /// A <c>transform</c> in percentages of the container, so the picture scales with its box.
    /// X is a share of the width, Y a share of the height, hence the two divisors.
    ///
    /// The <c>will-change</c> is here rather than in the stylesheet because this is exactly the set
    /// of layers that move: the WebView on the tablet keeps those on the GPU instead of promoting
    /// and dropping them at every step, and the static layers - most of the stack - stay cheap.
    /// </summary>
    public static string Move(double xMm, double yMm, double widthMm, double heightMm,
                             double scale = 1)
    {
        var x = xMm / widthMm * 100;
        var y = yMm / heightMm * 100;
        var style = string.Create(CultureInfo.InvariantCulture,
            $"will-change:transform;transform:translate({x:0.###}%,{y:0.###}%)");

        return scale is < 0.999 or > 1.001
            ? style + string.Create(CultureInfo.InvariantCulture, $" scale({scale:0.###})")
            : style;
    }

    public static string MoveTop(double xMm, double yMm, double scale = 1) =>
        Move(xMm, yMm, TopWidthMm, TopHeightMm, scale);

    public static string MoveFront(double xMm, double yMm, double scale = 1) =>
        Move(xMm, yMm, FrontWidthMm, FrontHeightMm, scale);

    /// <summary>
    /// Where one pallet stands. <see cref="Forward"/> is millimetres towards the front of the cell,
    /// counted from the place the drawing itself puts a pallet - the one number both views need.
    /// Seen from above it is a move down the picture; seen from the front it is depth.
    /// </summary>
    /// <summary>
    /// <paramref name="OnBelt"/> separates the one place the arm reaches into from the ones it
    /// reaches over: seen from the front, the arm passes in front of the side columns but goes
    /// behind the pallet waiting on the belt, the one it is about to pick up.
    /// </summary>
    public readonly record struct Placement(int Id, PalletInfo Pallet, double Sideways,
                                            double Forward, bool InGripper, bool OnBelt);

    /// <summary>
    /// Every pallet the cell knows about, and where it stands. A pallet in the gripper has already
    /// been taken out of its column, so it is drawn on the arm and follows it. The order is back to
    /// front, which is also the order the two views want to draw them in.
    /// </summary>
    public static IEnumerable<Placement> Placements(
        CellSnapshot cell, MachineSnapshot machine, PlcConfigSnapshot? config = null)
    {
        var distance = machine.Io.ConveyorRawDistance;
        var pitch = PitchMm(config);
        var found = new List<Placement>();

        foreach (var region in PlcEnums.AllRegions)
        {
            var row = cell.Rows[region];
            for (var i = 0; i < row.Count && i < RowState.MaxCapacity; i++)
            {
                var id = row.PalletIds[i];
                if (id == cell.InGripper) continue;

                var pallet = cell.PalletById(id);
                if (pallet is null) continue;

                var forward = region == Region.Center
                    ? BeltRowMm(distance, i, pitch)
                    : ColumnRowMm(i, row.IsAtFront);

                found.Add(new Placement(id, pallet, ColumnShift(region), forward,
                    InGripper: false, OnBelt: region == Region.Center));
            }
        }

        if (cell.GripperHoldsPallet && cell.PalletById(cell.InGripper) is { } held)
        {
            found.Add(new Placement(cell.InGripper, held,
                machine.Arm.Position - ArmBaselineMm, GripperRowMm,
                InGripper: true, OnBelt: false));
        }

        return found.OrderBy(p => p.Forward);
    }

    /// <summary>
    /// File name of the drawing for one object. The set that came from the old visualisation calls
    /// an object without a colour "unknown" and a pallet nobody has scanned yet "unverified";
    /// black was never drawn and is generated to the same geometry.
    /// </summary>
    public static string? PieceName(ObjectType type) => type switch
    {
        ObjectType.Missing => null,
        ObjectType.Unknown => "unverified",
        ObjectType.NoColor => "unknown",
        ObjectType.Red => "red",
        ObjectType.Green => "green",
        ObjectType.Cyan => "cyan",
        ObjectType.Gray => "gray",
        ObjectType.Orange => "orange",
        ObjectType.White => "white",
        ObjectType.Black => "black",
        _ => null
    };
}
