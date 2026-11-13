using Microsoft.AspNetCore.Mvc;
using QuadroSense.Api.Models;
using QuadroSense.Api.Services;

namespace QuadroSense.Api.Controllers;

[ApiController]
[Route("api/triggers")]
public sealed class TriggersController : ControllerBase
{
    private readonly ControllerOutputService _output;

    public TriggersController(ControllerOutputService output) => _output = output;

    /// <summary>
    /// Set adaptive trigger configuration for one side.
    /// Effect is applied immediately via HID output report.
    /// </summary>
    [HttpPut("{side}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    public IActionResult SetTrigger(TriggerSide side, [FromBody] AdaptiveTriggerConfig config)
    {
        _output.SetTrigger(side, config);
        return NoContent();
    }

    /// <summary>Reset both triggers to Off.</summary>
    [HttpDelete]
    [ProducesResponseType(204)]
    public IActionResult ResetTriggers()
    {
        _output.SetTrigger(TriggerSide.Left,  new AdaptiveTriggerConfig());
        _output.SetTrigger(TriggerSide.Right, new AdaptiveTriggerConfig());
        return NoContent();
    }
}
