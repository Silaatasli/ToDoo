using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Todoo.Business.Abstract;
using Todoo.Business.Models.Sprints;
using Todoo.WebApi.Helpers;

namespace Todoo.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public class SprintsController : ControllerBase
{
    private readonly ISprintService _sprintService;

    public SprintsController(ISprintService sprintService)
    {
        _sprintService = sprintService;
    }

    private bool TryGetUserId(out int userId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out userId);
    }

    [HttpGet("teams/{teamId:int}/boards/{boardId:int}/kapsam")]
    public async Task<IActionResult> GetKapsam(int teamId, int boardId)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        var result = await _sprintService.GetKapsamAsync(teamId, boardId, userId);
        return result.ToActionResult();
    }

    [HttpPost("teams/{teamId:int}/boards/{boardId:int}/sprints")]
    public async Task<IActionResult> Create(int teamId, int boardId, [FromBody] CreateSprintRequest request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        var result = await _sprintService.CreateAsync(teamId, boardId, request, userId);
        return result.ToActionResult(sprint => CreatedAtAction(nameof(GetById), new { sprintId = sprint.Id }, sprint));
    }

    [HttpGet("sprints/{sprintId:int}")]
    public async Task<IActionResult> GetById(int sprintId)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        var result = await _sprintService.GetByIdAsync(sprintId, userId);
        return result.ToActionResult();
    }

    [HttpPut("sprints/{sprintId:int}")]
    public async Task<IActionResult> Update(int sprintId, [FromBody] UpdateSprintRequest request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        var result = await _sprintService.UpdateAsync(sprintId, request, userId);
        return result.ToActionResult();
    }

    [HttpDelete("sprints/{sprintId:int}")]
    public async Task<IActionResult> Delete(int sprintId)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        var result = await _sprintService.DeleteAsync(sprintId, userId);
        return result.ToActionResult();
    }

    [HttpPost("sprints/{sprintId:int}/tasks/{taskId:int}")]
    public async Task<IActionResult> MoveTaskToSprint(
        int sprintId,
        int taskId,
        [FromBody] MoveTaskToSprintRequest? request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        var result = await _sprintService.MoveTaskToSprintAsync(
            sprintId,
            taskId,
            request ?? new MoveTaskToSprintRequest(),
            userId);
        return result.ToActionResult();
    }

    [HttpPost("taskitems/{taskId:int}/move-to-backlog")]
    public async Task<IActionResult> MoveTaskToBacklog(int taskId, [FromBody] MoveTaskToSprintRequest? request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        var result = await _sprintService.MoveTaskToBacklogAsync(taskId, request?.TargetIndex, userId);
        return result.ToActionResult();
    }

    [HttpPut("sprints/{sprintId:int}/tasks/reorder")]
    public async Task<IActionResult> ReorderSprintTasks(int sprintId, [FromBody] ReorderSprintTasksRequest request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        var result = await _sprintService.ReorderSprintTasksAsync(sprintId, request, userId);
        return result.ToActionResult();
    }

    [HttpPut("teams/{teamId:int}/boards/{boardId:int}/backlog/reorder")]
    public async Task<IActionResult> ReorderBacklog(int teamId, int boardId, [FromBody] ReorderSprintTasksRequest request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        var result = await _sprintService.ReorderBacklogAsync(boardId, request, userId);
        return result.ToActionResult();
    }

    [HttpPost("sprints/{sprintId:int}/start")]
    public async Task<IActionResult> Start(int sprintId)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        var result = await _sprintService.StartAsync(sprintId, userId);
        return result.ToActionResult();
    }

    [HttpPost("sprints/{sprintId:int}/complete")]
    public async Task<IActionResult> Complete(int sprintId, [FromBody] CompleteSprintRequest? request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        var result = await _sprintService.CompleteAsync(
            sprintId,
            request ?? new CompleteSprintRequest(),
            userId);
        return result.ToActionResult();
    }

    [HttpPost("sprints/{sprintId:int}/cancel")]
    public async Task<IActionResult> Cancel(int sprintId, [FromBody] CancelSprintRequest? request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        var result = await _sprintService.CancelAsync(
            sprintId,
            request ?? new CancelSprintRequest(),
            userId);
        return result.ToActionResult();
    }
}
