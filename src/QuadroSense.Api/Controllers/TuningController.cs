using Microsoft.AspNetCore.Mvc;
using QuadroSense.Api.Models;
using QuadroSense.Api.Services;

namespace QuadroSense.Api.Controllers;

[ApiController]
[Route("api/tuning")]
public sealed class TuningController : ControllerBase
{
    private readonly ProfileService _profiles;

    public TuningController(ProfileService profiles) => _profiles = profiles;

    /// <summary>
    /// Apply stick/touchpad/gyro tuning to the active profile.
    /// Tuning affects the virtual controller's input report processing,
    /// not the HID output report (no HID write needed here).
    /// </summary>
    [HttpPut]
    [ProducesResponseType<TuningConfig>(200)]
    [ProducesResponseType(409)]
    public IActionResult SetTuning([FromBody] TuningConfig config)
    {
        var active = _profiles.GetActiveProfile();
        if (active is null)
        {
            active = new Profile { Name = "Default", Tuning = config };
            _profiles.UpsertProfile(active);
            _profiles.ActivateProfile(active.Id);
            return Ok(config);
        }

        var updated = active with { Tuning = config };
        _profiles.UpsertProfile(updated);
        return Ok(config);
    }

    [HttpGet]
    [ProducesResponseType<TuningConfig>(200)]
    [ProducesResponseType(204)]
    public IActionResult GetTuning()
    {
        var active = _profiles.GetActiveProfile();
        return active is null ? NoContent() : Ok(active.Tuning);
    }
}
