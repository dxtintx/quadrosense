using Microsoft.AspNetCore.Mvc;
using QuadroSense.Api.Models;
using QuadroSense.Api.Services;

namespace QuadroSense.Api.Controllers;

[ApiController]
[Route("api/haptics")]
public sealed class HapticsController : ControllerBase
{
    private readonly ControllerOutputService _output;
    private readonly AudioHapticService      _audioHaptic;

    public HapticsController(ControllerOutputService output, AudioHapticService audioHaptic)
    {
        _output      = output;
        _audioHaptic = audioHaptic;
    }

    /// <summary>
    /// Configure haptics: static rumble levels and/or audio-to-haptic pipeline.
    /// Setting AudioToHapticEnabled=true starts WASAPI loopback capture.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(204)]
    public IActionResult SetHaptics([FromBody] HapticConfig config)
    {
        _output.SetHaptics(config);
        _audioHaptic.ApplyConfig(config);
        return NoContent();
    }

    /// <summary>Get current haptic pipeline state and metrics.</summary>
    [HttpGet("state")]
    [ProducesResponseType<HapticState>(200)]
    public IActionResult GetState() => Ok(_audioHaptic.State);
}
