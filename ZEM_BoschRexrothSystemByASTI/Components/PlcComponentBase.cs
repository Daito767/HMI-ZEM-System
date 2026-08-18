using Microsoft.AspNetCore.Components;
using ZEM_BoschRexrothSystemByASTI.Plc;

namespace ZEM_BoschRexrothSystemByASTI.Components;

/// <summary>
/// Redraws the page whenever the refresh loop brings new values. The loop runs on a background
/// thread, so every callback goes back through <see cref="ComponentBase.InvokeAsync(Action)"/>.
/// </summary>
public abstract class PlcComponentBase : ComponentBase, IDisposable
{
    private DateTime _lastRender = DateTime.MinValue;

    [Inject] protected PlcService Plc { get; set; } = default!;

    protected CellSnapshot Cell => Plc.Cell;
    protected MachineSnapshot Machine => Plc.Machine;
    protected DiagSnapshot Diag => Plc.Diag;
    protected PlcConfigSnapshot Config => Plc.Config;

    /// <summary>
    /// A guard against a loop set absurdly fast, not a frame rate. It sits below the shortest
    /// publishing interval the settings allow, because this throttle drops an update instead of
    /// deferring it: one dropped update is one step the drawing never makes, and then the next one
    /// covers twice the distance - which is the stutter, not a cure for it.
    /// </summary>
    protected virtual TimeSpan MinimumRenderInterval => TimeSpan.FromMilliseconds(40);

    protected override void OnInitialized()
    {
        Plc.Updated += OnPlcUpdated;
        Plc.LinkChanged += OnLinkChanged;
    }

    private void OnPlcUpdated()
    {
        var now = DateTime.UtcNow;
        if (now - _lastRender < MinimumRenderInterval) return;
        _lastRender = now;
        InvokeAsync(StateHasChanged);
    }

    private void OnLinkChanged() => InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        Plc.Updated -= OnPlcUpdated;
        Plc.LinkChanged -= OnLinkChanged;
        GC.SuppressFinalize(this);
    }
}
