namespace ZEM_BoschRexrothSystemByASTI.Plc;

/// <summary>
/// One thing wrong, in the words the operator sees. <see cref="Key"/> is what tells two alarms
/// apart when one is silenced and another turns up.
/// </summary>
public readonly record struct Alarm(string Key, string Text);

/// <summary>
/// What the HMI considers worth a sound. Kept apart from the pages because the sound has to work
/// whichever page is open, and because the list is the whole decision - the rest is plumbing.
/// </summary>
public static class Alarms
{
    public static IReadOnlyList<Alarm> Of(PlcService plc)
    {
        var found = new List<Alarm>();

        // A connection that was never made is not a fault: the HMI sits offline on purpose when
        // auto-connect is off, and an alarm at every start would be one nobody listens to.
        if (plc.LinkState == PlcLinkState.Faulted)
            found.Add(new Alarm("link", "LEGATURA PIERDUTA"));

        // Everything below is read from the cell, so it is only worth saying while the values are
        // still arriving. Without this, a dropped connection would freeze `Air_Presure_Ok` at its
        // last value and the HMI would go on claiming something it can no longer see.
        if (plc.LinkState != PlcLinkState.Online)
            return found;

        if (plc.Diag.Active)
            found.Add(new Alarm("halt", "OPRIRE — vezi diagnosticul"));

        // `Run` is no longer required as well: the PLC drops to manual by itself when the pressure
        // fails, so the condition holding the alarm up would be gone before anyone could see it.
        if (!plc.Machine.Io.AirPressureOk)
            found.Add(new Alarm("air", "FARA AER IN SISTEM"));

        if (plc.Machine.Arm.HasError)
            found.Add(new Alarm("arm", "EROARE LA AXA BRATULUI"));

        if (plc.Machine.Conveyor.HasError)
            found.Add(new Alarm("conveyor", "EROARE LA AXA BENZII"));

        return found;
    }
}
