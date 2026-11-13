using Microsoft.AspNetCore.Mvc;
using QuadroSense.Api.Models;
using QuadroSense.Api.Services;

namespace QuadroSense.Api.Controllers;

[ApiController]
[Route("api/remapping")]
public sealed class RemappingController : ControllerBase
{
    private readonly RemappingService _remap;

    public RemappingController(RemappingService remap) => _remap = remap;

    // ── Button bindings ───────────────────────────────────────────────────────

    [HttpGet("bindings")]
    [ProducesResponseType<IReadOnlyList<ButtonBinding>>(200)]
    public IActionResult GetBindings() => Ok(_remap.GetBindings());

    [HttpPut("bindings")]
    [ProducesResponseType(204)]
    public IActionResult SetBindings([FromBody] List<ButtonBinding> bindings)
    {
        _remap.SetBindings(bindings);
        return NoContent();
    }

    // ── Macros ────────────────────────────────────────────────────────────────

    [HttpGet("macros")]
    [ProducesResponseType<IReadOnlyList<Macro>>(200)]
    public IActionResult GetMacros() => Ok(_remap.GetMacros());

    [HttpGet("macros/{id}")]
    [ProducesResponseType<Macro>(200)]
    [ProducesResponseType(404)]
    public IActionResult GetMacro(string id)
    {
        var m = _remap.GetMacro(id);
        return m is null ? NotFound() : Ok(m);
    }

    [HttpPut("macros/{id}")]
    [ProducesResponseType<Macro>(200)]
    public IActionResult UpsertMacro(string id, [FromBody] Macro macro) =>
        Ok(_remap.UpsertMacro(macro with { Id = id }));

    [HttpDelete("macros/{id}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public IActionResult DeleteMacro(string id) =>
        _remap.DeleteMacro(id) ? NoContent() : NotFound();

    /// <summary>Trigger a macro playback immediately (async, non-blocking).</summary>
    [HttpPost("macros/{id}/trigger")]
    [ProducesResponseType(202)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> TriggerMacro(string id)
    {
        bool ok = await _remap.TriggerMacroAsync(id);
        return ok ? Accepted() : NotFound();
    }
}
