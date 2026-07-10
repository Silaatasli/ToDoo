using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Todoo.Business.Abstract;
using Todoo.WebApi.Helpers;

namespace Todoo.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;

    public ReportsController(IReportService reportService)
    {
        _reportService = reportService;
    }

    [HttpGet("task-summary")]
    public async Task<IActionResult> GetTaskSummary([FromQuery] int teamId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        if (teamId <= 0)
        {
            return BadRequest(new { success = false, message = "teamId parametresi zorunludur ve 0'dan buyuk olmalidir." });
        }

        var result = await _reportService.GetTaskReportByTeamIdAsync(teamId, userId);
        return result.ToActionResult();
    }
}
