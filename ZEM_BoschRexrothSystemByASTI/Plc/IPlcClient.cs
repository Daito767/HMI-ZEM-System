namespace ZEM_BoschRexrothSystemByASTI.Plc;

/// <summary>
/// The only four writable variables. Everything else in the address space is read-only,
/// and that is a choice: the model is kept in step with the machine by the motion methods
/// in the PLC, and a write from the network would desynchronise it silently.
/// </summary>
public enum PlcCommand
{
    Start,
    Reset,
    Pause,
    EndStepPause
}

public static class PlcCommands
{
    public static string Symbol(this PlcCommand command) => command switch
    {
        PlcCommand.Start => PlcSymbols.StartCommand,
        PlcCommand.Reset => PlcSymbols.ResetCommand,
        PlcCommand.Pause => PlcSymbols.PauseCommand,
        PlcCommand.EndStepPause => PlcSymbols.EndStepPauseCommand,
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    public static string Label(this PlcCommand command) => command switch
    {
        PlcCommand.Start => "Start",
        PlcCommand.Reset => "Reset",
        PlcCommand.Pause => "Pauza",
        PlcCommand.EndStepPause => "Pauza la final de pas",
        _ => command.ToString()
    };
}

public enum PlcLinkState
{
    Offline,
    Connecting,
    Online,
    Faulted
}

/// <summary>One logical variable and where it landed in the server address space.</summary>
public sealed record SymbolBinding(string LogicalName, string? NodeId, bool Bound, string? Note)
{
    public static SymbolBinding Missing(string logicalName, string? note = null) =>
        new(logicalName, null, false, note);
}

/// <summary>
/// What the HMI needs from a cell, whether it comes over OPC UA or from the simulator.
/// Implementations fill the caller's snapshot in place so the UI keeps one live object.
/// </summary>
public interface IPlcClient : IAsyncDisposable
{
    string Description { get; }
    bool IsConnected { get; }

    /// <summary>Bindings resolved at connect time, for the symbol inspector page.</summary>
    IReadOnlyList<SymbolBinding> Bindings { get; }

    Task ConnectAsync(CancellationToken ct);
    Task DisconnectAsync();

    /// <summary>The refresh loop: Main state plus Layout, without the heavy nodes.</summary>
    Task ReadCellAsync(CellSnapshot target, CancellationToken ct);

    /// <summary>The refresh loop: the axes, the process image and the command layer's own state.</summary>
    Task ReadMachineAsync(MachineSnapshot target, CancellationToken ct);

    /// <summary>The two sorting tables. They only change when someone edits them.</summary>
    Task ReadPoliciesAsync(PolicyState target, CancellationToken ct);

    Task WriteBoolAsync(string symbol, bool value, CancellationToken ct);
    Task WriteRealAsync(string symbol, double value, CancellationToken ct);
    Task WriteIntAsync(string symbol, int value, CancellationToken ct);

    /// <summary>
    /// Writes FALSE to every level flag in one request. There is no watchdog in the PLC, so this is
    /// what stands between a dropped connection and an axis that keeps moving.
    /// </summary>
    Task ClearCommandFlagsAsync(CancellationToken ct);

    /// <summary>The refresh loop: <c>Diag.Active</c>, <c>Diag.Last.*</c>, <c>Diag.Count</c>.</summary>
    Task ReadDiagAsync(DiagSnapshot target, CancellationToken ct);

    /// <summary>32 x STRING(60), ~2 KB. Read it when the diagnostics panel opens, not in the loop.</summary>
    Task ReadDiagHistoryAsync(DiagSnapshot target, CancellationToken ct);

    /// <summary>~1.9 KB of colour names. The HMI translates the enum itself, so this is on demand only.</summary>
    Task ReadColorNamesAsync(CellSnapshot target, CancellationToken ct);

    /// <summary>The stand's numbers. They do not change while the cell runs, so read once.</summary>
    Task ReadConfigAsync(PlcConfigSnapshot target, CancellationToken ct);

    /// <summary>Writes TRUE. The PLC clears the flag itself once it has consumed it.</summary>
    Task SendCommandAsync(PlcCommand command, CancellationToken ct);
}
