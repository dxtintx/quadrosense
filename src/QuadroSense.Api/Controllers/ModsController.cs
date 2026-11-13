using Microsoft.AspNetCore.Mvc;
using QuadroSense.Api.Models;
using QuadroSense.Api.Services;

namespace QuadroSense.Api.Controllers;

/// <summary>
/// Mod system API — games and community plugins register here and push
/// real-time override events for triggers, haptics, and LEDs.
///
/// Typical mod lifecycle:
///   POST /api/mods/register        → get modId
///   POST /api/mods/{modId}/event   → push override (fires HID output immediately)
///   DELETE /api/mods/{modId}       → unregister on game exit
/// </summary>
[ApiController]
[Route("api/mods")]
public sealed class ModsController : ControllerBase
{
    private readonly ModService _mods;

    public ModsController(ModService mods) => _mods = mods;

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ModRegistration>>(200)]
    public IActionResult List() => Ok(_mods.ListRegistrations());

    [HttpPost("register")]
    [ProducesResponseType<ModRegistration>(201)]
    public IActionResult Register([FromBody] ModRegistration reg)
    {
        var result = _mods.Register(reg);
        return CreatedAtAction(nameof(List), new { }, result);
    }

    [HttpDelete("{modId}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public IActionResult Unregister(string modId) =>
        _mods.Unregister(modId) ? NoContent() : NotFound();

    /// <summary>
    /// Push a real-time control override event.
    /// All fields are optional — only specified ones are overridden.
    /// DurationMs=0 means one-shot; -1 means persistent until next event.
    /// </summary>
    [HttpPost("{modId}/event")]
    [ProducesResponseType(202)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> PushEvent(string modId, [FromBody] ModEvent ev)
    {
        var hydrated = ev with { ModId = modId };
        bool ok = await _mods.PushEventAsync(hydrated);
        return ok ? Accepted() : NotFound("Mod not registered.");
    }
}
