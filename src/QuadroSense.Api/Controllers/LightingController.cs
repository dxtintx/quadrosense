using Microsoft.AspNetCore.Mvc;
using QuadroSense.Api.Models;
using QuadroSense.Api.Services;

namespace QuadroSense.Api.Controllers;

[ApiController]
[Route("api/lighting")]
public sealed class LightingController : ControllerBase
{
    private readonly ControllerOutputService _output;

    public LightingController(ControllerOutputService output) => _output = output;

    /// <summary>
    /// Set lightbar color, player LEDs, and dynamic effects.
    /// "audio_reactive" uses the audio pipeline RMS to pulse the lightbar.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(204)]
    public IActionResult SetLighting([FromBody] LightingConfig config)
    {
        _output.SetLighting(config);
        return NoContent();
    }

    /// <summary>
    /// Sets active player count (1-4) and resets lightbar to default PlayStation Blue.
    /// Cancels any charging indicator.
    /// </summary>
    [HttpPost("player/{playerNumber}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> SetPlayer(int playerNumber)
    {
        await _output.SetDefaultConnectedStateAsync(Math.Clamp(playerNumber, 1, 4));
        return NoContent();
    }

    /// <summary>
    /// Resets lighting to default PlayStation Blue, Player 1 LED, mic off.
    /// </summary>
    [HttpPost("reset")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Reset()
    {
        await _output.SetDefaultConnectedStateAsync(1);
        return NoContent();
    }
}
