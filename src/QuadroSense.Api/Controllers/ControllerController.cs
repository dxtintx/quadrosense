using Microsoft.AspNetCore.Mvc;
using QuadroSense.Api.HID;
using QuadroSense.Api.Models;
using QuadroSense.Api.Services;

namespace QuadroSense.Api.Controllers;

[ApiController]
[Route("api/controller")]
public sealed class ControllerController : ControllerBase
{
    private readonly ControllerOutputService  _output;
    private readonly ControllerDeviceManager  _deviceManager;
    private readonly ControllerAudioService   _audioService;

    public ControllerController(
        ControllerOutputService output,
        ControllerDeviceManager deviceManager,
        ControllerAudioService audioService)
    {
        _output        = output;
        _deviceManager = deviceManager;
        _audioService  = audioService;
    }

    /// <summary>List all connected DS5 controllers.</summary>
    [HttpGet("devices")]
    [ProducesResponseType<IReadOnlyList<string>>(200)]
    public IActionResult GetDevices() =>
        Ok(ControllerDeviceManager.EnumerateDevices());

    /// <summary>Connect to a physical controller (empty path = auto-detect).</summary>
    [HttpPost("connect")]
    [ProducesResponseType<ControllerDiagnostics>(200)]
    [ProducesResponseType(404)]
    public IActionResult Connect([FromQuery] string? devicePath = null)
    {
        bool ok = _output.TryConnect(devicePath);
        return ok ? Ok(_output.GetDiagnostics()) : NotFound("No DS5 controller found.");
    }

    /// <summary>Get live diagnostics: battery, RSSI, firmware, counters.</summary>
    [HttpGet("diagnostics")]
    [ProducesResponseType<ControllerDiagnostics>(200)]
    public IActionResult GetDiagnostics() => Ok(_output.GetDiagnostics());

    [HttpGet("configuration")]
    [ProducesResponseType<ControllerConfiguration>(200)]
    public IActionResult GetConfiguration() => Ok(_output.GetConfiguration());

    [HttpGet("motion")]
    [ProducesResponseType<ControllerMotion>(200)]
    public IActionResult GetMotion() => Ok(_deviceManager.Motion);

    /// <summary>Plays a plain test tone directly through the DualSense onboard speaker.</summary>
    [HttpPost("sound/tone")]
    [ProducesResponseType(200)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> PlayTone([FromBody] ToneRequest? request, CancellationToken ct)
    {
        var req = request ?? new ToneRequest();
        bool ok = await _audioService.PlayToneAsync(req, ct);
        return ok ? Ok(new { Success = true, Message = $"Played {req.FrequencyHz}Hz tone on controller speaker" })
                  : StatusCode(500, "Failed to render tone to audio endpoint");
    }
}
