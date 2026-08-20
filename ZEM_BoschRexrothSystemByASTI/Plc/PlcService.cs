using Microsoft.Extensions.Logging;

namespace ZEM_BoschRexrothSystemByASTI.Plc;

/// <summary>
/// Owns the connection and the refresh loop, and hands one live snapshot to every page.
///
/// Two nodes stay out of the loop on purpose (OPCUA-HMI.md §6): <c>Diag.History</c> is ~2 KB and
/// <c>Pool[*]._objectColorsStr</c> is 88% of the Layout structure. Both are read on demand.
/// </summary>
public sealed class PlcService : IAsyncDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    private readonly HmiSettingsStore _settingsStore;
    private readonly ILogger<PlcService> _log;
    private readonly SemaphoreSlim _clientGate = new(1, 1);

    private IPlcClient? _client;
    private CancellationTokenSource? _loopCancellation;
    private Task? _loop;
    private volatile bool _connectRequested;

    public PlcService(HmiSettingsStore settingsStore, ILogger<PlcService> log)
    {
        _settingsStore = settingsStore;
        _log = log;
    }

    public CellSnapshot Cell { get; } = new();
    public MachineSnapshot Machine { get; } = new();
    public DiagSnapshot Diag { get; } = new();
    public PlcConfigSnapshot Config { get; } = new();
    public PolicyState Policies { get; } = new();

    public PlcLinkState LinkState { get; private set; } = PlcLinkState.Offline;
    public string? LastError { get; private set; }
    public string Endpoint => _client?.Description ?? _settingsStore.Current.EndpointUrl;
    public bool IsSimulated => _client is SimulatedPlcClient;

    /// <summary>True while the server pushes the loop values instead of the loop asking for them.</summary>
    public bool IsLive => _client is OpcUaPlcClient { IsLive: true };

    /// <summary>
    /// How often a new value can arrive. With a subscription it is the publishing interval; without
    /// one it is the poll interval plus whatever the network adds. The drawings take their step
    /// length from here, so the movement lasts exactly as long as the wait for the next value.
    /// </summary>
    public int UpdatePeriodMs => _client is OpcUaPlcClient { IsLive: true } live
        ? live.PublishingIntervalMs
        : Math.Max(50, _settingsStore.Current.PollIntervalMs);

    /// <summary>
    /// When the server pushes, the loop waits for the push rather than for a clock of its own. Two
    /// clocks that are not the same clock drift against each other, and the drift lands in the
    /// picture: one step arrives fresh, the next has been waiting, so the movement comes out in
    /// uneven jumps however fast the values are.
    ///
    /// The timeout is only a way out. If the publishing stops, the wait ends, the next cycle finds
    /// the subscription no longer live, and everything goes back to reading.
    /// </summary>
    private Task WaitForWorkAsync(CancellationToken ct) =>
        _client is OpcUaPlcClient { IsLive: true } live
            ? live.WaitForPublishAsync(Math.Max(1_000, live.PublishingIntervalMs * 4), ct)
            : Task.Delay(Math.Max(50, _settingsStore.Current.PollIntervalMs), ct);

    public IReadOnlyList<SymbolBinding> Bindings => _client?.Bindings ?? Array.Empty<SymbolBinding>();
    public string? ApplicationPath => (_client as OpcUaPlcClient)?.ApplicationPath;
    public int LiveNodeCount => (_client as OpcUaPlcClient)?.LiveNodeCount ?? 0;

    /// <summary>The sampling interval the server granted, which may be slower than the one asked.</summary>
    public double SamplingIntervalMs => (_client as OpcUaPlcClient)?.SamplingIntervalMs ?? 0;

    /// <summary>The measured spread of the publishes, for the symbol page.</summary>
    public (double Min, double Average, double Max, int Count) PublishGaps =>
        (_client as OpcUaPlcClient)?.PublishGaps ?? (0, 0, 0, 0);
    public string? SubscriptionError => (_client as OpcUaPlcClient)?.SubscriptionError;
    public IReadOnlyCollection<BrowsedVariable> BrowsedVariables =>
        (_client as OpcUaPlcClient)?.BrowsedVariables ?? Array.Empty<BrowsedVariable>();

    /// <summary>Raised after every successful refresh. Handlers must marshal to the UI thread themselves.</summary>
    public event Action? Updated;

    /// <summary>Raised when the connection state or the last error changes.</summary>
    public event Action? LinkChanged;

    /// <summary>Set when a command was written, so the UI can acknowledge it.</summary>
    public (PlcCommand Command, DateTime At)? LastCommand { get; private set; }

    // --- connection -------------------------------------------------------------------

    public async Task StartAsync()
    {
        if (_loop is not null) return;

        await _settingsStore.LoadPasswordAsync();

        _loopCancellation = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_loopCancellation.Token));
    }

    /// <summary>Drops the connection so the loop builds a new client from the current settings.</summary>
    public async Task ReconnectAsync()
    {
        await _clientGate.WaitAsync();
        try
        {
            await DisposeClientAsync();
            SetLink(PlcLinkState.Offline, null);
            // Asking for a reconnect is a manual connect, even with auto-connect switched off.
            _connectRequested = true;
        }
        finally
        {
            _clientGate.Release();
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var settings = _settingsStore.Current;

            try
            {
                if (_client is null || !_client.IsConnected)
                {
                    if (!settings.AutoConnect && !_connectRequested && LinkState == PlcLinkState.Offline)
                    {
                        await Task.Delay(500, ct);
                        continue;
                    }

                    _connectRequested = false;
                    await ConnectClientAsync(settings, ct);
                }

                await RefreshAsync(ct);
                await WaitForWorkAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "bucla PLC a esuat");
                SetLink(PlcLinkState.Faulted, ex.Message);
                await _clientGate.WaitAsync(ct);
                try { await DisposeClientAsync(); }
                finally { _clientGate.Release(); }

                try { await Task.Delay(RetryDelay, ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task ConnectClientAsync(HmiSettings settings, CancellationToken ct)
    {
        await _clientGate.WaitAsync(ct);
        try
        {
            await DisposeClientAsync();
            SetLink(PlcLinkState.Connecting, null);

            _client = settings.UseSimulator
                ? new SimulatedPlcClient()
                : new OpcUaPlcClient(settings, FileSystem.AppDataDirectory, _log);

            await _client.ConnectAsync(ct);
            SetLink(PlcLinkState.Online, null);
        }
        finally
        {
            _clientGate.Release();
        }

        // The stand's numbers do not change while it runs, so they are read once per connection.
        try { await _client.ReadConfigAsync(Config, ct); }
        catch (Exception ex) { _log.LogWarning(ex, "citirea GVL_Config a esuat"); }

        try { await _client.ReadPoliciesAsync(Policies, ct); }
        catch (Exception ex) { _log.LogWarning(ex, "citirea tabelelor de sortare a esuat"); }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var client = _client;
        if (client is null) return;

        await client.ReadCellAsync(Cell, ct);
        await client.ReadMachineAsync(Machine, ct);
        await client.ReadDiagAsync(Diag, ct);
        Updated?.Invoke();
    }

    // --- on demand reads --------------------------------------------------------------

    public async Task<bool> RefreshHistoryAsync(CancellationToken ct = default)
    {
        var client = _client;
        if (client is null || !client.IsConnected) return false;
        try
        {
            await client.ReadDiagHistoryAsync(Diag, ct);
            Updated?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "citirea Diag.History a esuat");
            SetLink(LinkState, ex.Message);
            return false;
        }
    }

    public async Task<bool> RefreshColorNamesAsync(CancellationToken ct = default)
    {
        var client = _client;
        if (client is null || !client.IsConnected) return false;
        try
        {
            await client.ReadColorNamesAsync(Cell, ct);
            Updated?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "citirea numelor de culori a esuat");
            SetLink(LinkState, ex.Message);
            return false;
        }
    }

    public async Task<bool> RefreshConfigAsync(CancellationToken ct = default)
    {
        var client = _client;
        if (client is null || !client.IsConnected) return false;
        try
        {
            await client.ReadConfigAsync(Config, ct);
            Updated?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "citirea GVL_Config a esuat");
            SetLink(LinkState, ex.Message);
            return false;
        }
    }

    // --- commands ---------------------------------------------------------------------

    public bool CanSendCommands => _client is { IsConnected: true } && LinkState == PlcLinkState.Online;

    public async Task<bool> SendAsync(PlcCommand command, CancellationToken ct = default)
    {
        var client = _client;
        if (client is null || !client.IsConnected)
        {
            SetLink(LinkState, "nu exista conexiune; comanda nu a fost trimisa");
            return false;
        }

        try
        {
            await client.SendCommandAsync(command, ct);
            LastCommand = (command, DateTime.Now);
            LinkChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "comanda {Command} a esuat", command);
            SetLink(LinkState, ex.Message);
            return false;
        }
    }

    // --- manual commands ----------------------------------------------------------------

    /// <summary>
    /// The PLC only acts on the manual commands while the cell is neither running nor resetting
    /// (<c>HMI.impl</c>), so a button that looks live during a cycle would look broken instead.
    /// </summary>
    public bool CanCommandManually =>
        CanSendCommands && !Cell.Run && !Cell.ResetStarted;

    /// <summary>
    /// Raises or drops a level flag. Dropping one is never refused: the flag has to be able to go
    /// down even if the cell started running in the meantime.
    /// </summary>
    public async Task<bool> SetFlagAsync(string symbol, bool value, CancellationToken ct = default)
    {
        var client = _client;
        if (client is null || !client.IsConnected) return false;
        if (value && !CanCommandManually) return false;

        try
        {
            await client.WriteBoolAsync(symbol, value, ct);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "scrierea in {Symbol} a esuat", symbol);
            SetLink(LinkState, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// TRUE followed by FALSE. Most flags in <c>HMI</c> are level, not pulse, and the PLC clears
    /// only four of them - a move flag left up re-arms the move every cycle.
    /// </summary>
    public async Task<bool> PulseFlagAsync(string symbol, CancellationToken ct = default)
    {
        if (!await SetFlagAsync(symbol, true, ct)) return false;
        return await SetFlagAsync(symbol, false, ct);
    }

    public async Task<bool> SetRealAsync(string symbol, double value, CancellationToken ct = default)
    {
        var client = _client;
        if (client is null || !client.IsConnected || !CanCommandManually) return false;

        try
        {
            await client.WriteRealAsync(symbol, value, ct);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "scrierea in {Symbol} a esuat", symbol);
            SetLink(LinkState, ex.Message);
            return false;
        }
    }

    public async Task<bool> SetIntAsync(string symbol, int value, CancellationToken ct = default)
    {
        var client = _client;
        if (client is null || !client.IsConnected) return false;

        try
        {
            await client.WriteIntAsync(symbol, value, ct);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "scrierea in {Symbol} a esuat", symbol);
            SetLink(LinkState, ex.Message);
            return false;
        }
    }

    /// <summary>Drops every command flag. Called when the window loses focus, is hidden or closes.</summary>
    public async Task ReleaseAllFlagsAsync(CancellationToken ct = default)
    {
        var client = _client;
        if (client is null || !client.IsConnected) return;

        try { await client.ClearCommandFlagsAsync(ct); }
        catch (Exception ex) { _log.LogWarning(ex, "stingerea steagurilor a esuat"); }
    }

    public async Task<bool> RefreshPoliciesAsync(CancellationToken ct = default)
    {
        var client = _client;
        if (client is null || !client.IsConnected) return false;
        try
        {
            await client.ReadPoliciesAsync(Policies, ct);
            Updated?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "citirea tabelelor de sortare a esuat");
            return false;
        }
    }

    // --- plumbing ---------------------------------------------------------------------

    private void SetLink(PlcLinkState state, string? error)
    {
        LinkState = state;
        LastError = error;
        LinkChanged?.Invoke();
    }

    private async Task DisposeClientAsync()
    {
        var client = _client;
        _client = null;
        if (client is null) return;

        // There is no watchdog in the PLC. A jog flag left TRUE means the axis keeps going until it
        // hits its travel limit, so this is the last thing to try before letting the session go.
        if (client.IsConnected)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await client.ClearCommandFlagsAsync(timeout.Token);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "nu s-au putut stinge steagurile de comanda inainte de deconectare");
            }
        }

        try { await client.DisposeAsync(); }
        catch (Exception ex) { _log.LogDebug(ex, "inchiderea clientului a esuat"); }
    }

    public async ValueTask DisposeAsync()
    {
        _loopCancellation?.Cancel();
        if (_loop is not null)
        {
            try { await _loop; }
            catch (OperationCanceledException) { }
        }

        await DisposeClientAsync();
        _loopCancellation?.Dispose();
        _clientGate.Dispose();
    }
}
