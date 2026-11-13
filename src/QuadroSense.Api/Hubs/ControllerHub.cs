using Microsoft.AspNetCore.SignalR;
using QuadroSense.Api.Models;
using QuadroSense.Api.Services;

namespace QuadroSense.Api.Hubs;

/// <summary>
/// Real-time WebSocket hub.
/// Clients subscribe to controller diagnostics (battery, RSSI) and
/// haptic pipeline metrics without polling REST endpoints.
///
/// Used by: external UIs, debug tools, and mods that need telemetry.
/// Protocol: SignalR (JSON over WebSocket; binary protocol optional).
/// </summary>
public sealed class ControllerHub : Hub
{
    private readonly ControllerOutputService _output;
    private readonly AudioHapticService      _audioHaptic;

    public ControllerHub(ControllerOutputService output, AudioHapticService audioHaptic)
    {
        _output      = output;
        _audioHaptic = audioHaptic;
    }

    /// <summary>Client calls this to request a one-time diagnostics snapshot.</summary>
    public ControllerDiagnostics GetDiagnostics() => _output.GetDiagnostics();

    /// <summary>Client calls this to request current haptic state.</summary>
    public HapticState GetHapticState() => _audioHaptic.State;
}

/// <summary>
/// Background broadcaster — pushes diagnostics to all hub clients every second.
/// </summary>
public sealed class DiagnosticsBroadcaster : BackgroundService
{
    private readonly IHubContext<ControllerHub> _hub;
    private readonly ControllerOutputService    _output;
    private readonly AudioHapticService         _audioHaptic;

    public DiagnosticsBroadcaster(
        IHubContext<ControllerHub> hub,
        ControllerOutputService output,
        AudioHapticService audioHaptic)
    {
        _hub         = hub;
        _output      = output;
        _audioHaptic = audioHaptic;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken).ConfigureAwait(false);
            await _hub.Clients.All.SendAsync("diagnostics", _output.GetDiagnostics(), stoppingToken);
            await _hub.Clients.All.SendAsync("hapticState", _audioHaptic.State, stoppingToken);
        }
    }
}
