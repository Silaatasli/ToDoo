using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Todoo.Business.Abstract;
using Todoo.Entities.Entities;
using Todoo.WebApi.Helpers;
using Todoo.WebApi.Models.Teams;

namespace Todoo.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class TaskItemsController : ControllerBase
{
    private readonly ITaskService _taskService;
    private readonly ITaskAttachmentService _taskAttachmentService;

    public TaskItemsController(ITaskService taskService, ITaskAttachmentService taskAttachmentService)
    {
        _taskService = taskService;
        _taskAttachmentService = taskAttachmentService;
    }

    private bool TryGetUserId(out int userId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out userId);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _taskService.GetTaskDetailAsync(id, userId);
        return result.ToActionResult();
    }

    [HttpGet("{id:int}/activity")]
    public async Task<IActionResult> GetActivity(int id)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _taskService.GetTaskActivityAsync(id, userId);
        return result.ToActionResult();
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTeamTaskRequestDto request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var task = new TaskItem
        {
            Id = id,
            Title = request.Title,
            Description = request.Description,
            CategoryId = request.CategoryId,
            Priority = request.Priority,
            StartDate = request.StartDate,
            DueDate = request.DueDate
        };

        var result = await _taskService.UpdateTaskAsync(task, userId);
        return result.ToActionResult();
    }

    [HttpPatch("{id:int}/column")]
    public async Task<IActionResult> MoveToColumn(int id, [FromBody] MoveTaskColumnRequestDto request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _taskService.MoveTaskToColumnAsync(id, request.BoardColumnId, userId);
        return result.ToActionResult();
    }

    [HttpPatch("{id:int}/complete")]
    public async Task<IActionResult> Complete(int id)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _taskService.CompleteTaskAsync(id, userId);
        return result.ToActionResult();
    }

    [HttpPatch("{id:int}/reopen")]
    public async Task<IActionResult> Reopen(int id)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _taskService.ReopenTaskAsync(id, userId);
        return result.ToActionResult();
    }

    [HttpPatch("{id:int}/assign")]
    public async Task<IActionResult> Assign(int id, [FromBody] AssignTaskRequestDto request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _taskService.AssignTaskAsync(id, request.AssignedToUserId, userId);
        return result.ToActionResult();
    }

    [HttpPost("{id:int}/accept-assignment")]
    public async Task<IActionResult> AcceptAssignment(int id)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _taskService.AcceptAssignmentAsync(id, userId);
        return result.ToActionResult();
    }

    [HttpPost("{id:int}/decline-assignment")]
    public async Task<IActionResult> DeclineAssignment(int id)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _taskService.DeclineAssignmentAsync(id, userId);
        return result.ToActionResult();
    }

    [HttpGet("{taskId:int}/attachments")]
    public async Task<IActionResult> ListAttachments(int taskId)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _taskAttachmentService.ListAsync(taskId, userId);
        return result.ToActionResult();
    }

    [HttpPost("{taskId:int}/attachments")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadAttachment(int taskId, IFormFile file)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(new { success = false, message = "Dosya secilmedi." });
        }

        await using var stream = file.OpenReadStream();
        var result = await _taskAttachmentService.UploadAsync(
            taskId,
            file.FileName,
            file.ContentType,
            file.Length,
            stream,
            userId);

        return result.ToActionResult(created => Ok(created));
    }

    [HttpGet("{taskId:int}/attachments/{attachmentId:int}/download")]
    public async Task<IActionResult> DownloadAttachment(int taskId, int attachmentId)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _taskAttachmentService.DownloadAsync(taskId, attachmentId, userId);
        if (!result.Success)
        {
            return result.ToActionResult();
        }

        var (stream, contentType, fileName) = result.Data!;
        return File(stream, contentType, fileName);
    }

    [HttpDelete("{taskId:int}/attachments/{attachmentId:int}")]
    public async Task<IActionResult> DeleteAttachment(int taskId, int attachmentId)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _taskAttachmentService.DeleteAsync(taskId, attachmentId, userId);
        return result.ToActionResult();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _taskService.DeleteTaskAsync(id, userId);
        return result.ToActionResult();
    }
}
