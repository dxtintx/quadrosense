using Microsoft.AspNetCore.Mvc;
using QuadroSense.Api.Models;
using QuadroSense.Api.Services;

namespace QuadroSense.Api.Controllers;

[ApiController]
[Route("api/profiles")]
public sealed class ProfilesController : ControllerBase
{
    private readonly ProfileService _profiles;

    public ProfilesController(ProfileService profiles) => _profiles = profiles;

    /// <summary>List all saved profiles (summary only).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ProfileSummary>>(200)]
    public IActionResult List() => Ok(_profiles.ListProfiles());

    /// <summary>Get a full profile by ID.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType<Profile>(200)]
    [ProducesResponseType(404)]
    public IActionResult Get(string id)
    {
        var profile = _profiles.GetProfile(id);
        return profile is null ? NotFound() : Ok(profile);
    }

    /// <summary>Create or update a profile.</summary>
    [HttpPut("{id}")]
    [ProducesResponseType<Profile>(200)]
    public IActionResult Upsert(string id, [FromBody] Profile profile)
    {
        // Ensure route ID is authoritative
        var updated = profile with { Id = id };
        return Ok(_profiles.UpsertProfile(updated));
    }

    /// <summary>Delete a profile.</summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public IActionResult Delete(string id) =>
        _profiles.DeleteProfile(id) ? NoContent() : NotFound();

    /// <summary>
    /// Activate a profile immediately — applies all its settings to the controller.
    /// </summary>
    [HttpPost("{id}/activate")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public IActionResult Activate(string id) =>
        _profiles.ActivateProfile(id) ? NoContent() : NotFound();

    /// <summary>Get the currently active profile.</summary>
    [HttpGet("active")]
    [ProducesResponseType<Profile>(200)]
    [ProducesResponseType(204)]
    public IActionResult GetActive()
    {
        var p = _profiles.GetActiveProfile();
        return p is null ? NoContent() : Ok(p);
    }
}
