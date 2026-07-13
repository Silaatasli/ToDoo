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
public class TeamsController : ControllerBase
{
    private readonly ITeamService _teamService;
    private readonly ITaskService _taskService;

    public TeamsController(ITeamService teamService, ITaskService taskService)
    {
        _teamService = teamService;
        _taskService = taskService;
    }

    private bool TryGetUserId(out int userId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out userId);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTeamRequestDto request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _teamService.CreateTeamAsync(request.Name, request.BoardName, request.ColumnTitles, userId);
        return result.ToActionResult(team => CreatedAtAction(nameof(GetById), new { id = team.Id }, team));
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var teams = await _teamService.GetTeamsForUserAsync(userId);
        return Ok(teams);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _teamService.GetTeamByIdAsync(id, userId);
        return result.ToActionResult();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _teamService.DeleteTeamAsync(id, userId);
        return result.ToActionResult();
    }

    [HttpGet("{id:int}/boards")]
    public async Task<IActionResult> GetBoards(int id)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _teamService.GetBoardsAsync(id, userId);
        return result.ToActionResult();
    }

    [HttpPost("{id:int}/boards")]
    public async Task<IActionResult> CreateBoard(int id, [FromBody] CreateBoardRequestDto request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _teamService.CreateBoardAsync(id, request.Name, request.ColumnTitles, userId);
        return result.ToActionResult();
    }

    [HttpDelete("{id:int}/boards/{boardId:int}")]
    public async Task<IActionResult> DeleteBoard(int id, int boardId)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _teamService.DeleteBoardAsync(id, boardId, userId);
        return result.ToActionResult();
    }

    [HttpGet("{id:int}/boards/{boardId:int}")]
    public async Task<IActionResult> GetBoardById(int id, int boardId)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _teamService.GetBoardAsync(id, boardId, userId);
        return result.ToActionResult();
    }

    /// <summary>Backward-compatible: returns the first board by DisplayOrder. </summary>
    [HttpGet("{id:int}/board")]
    public async Task<IActionResult> GetBoard(int id)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _teamService.GetTeamBoardAsync(id, userId);
        return result.ToActionResult();
    }

    [HttpPost("{id:int}/boards/{boardId:int}/columns")]
    public async Task<IActionResult> AddColumn(int id, int boardId, [FromBody] AddBoardColumnRequestDto request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _teamService.AddBoardColumnAsync(id, boardId, request.Title, userId);
        return result.ToActionResult();
    }

    [HttpPut("{id:int}/boards/{boardId:int}/columns/{columnId:int}")]
    public async Task<IActionResult> UpdateColumn(int id, int boardId, int columnId, [FromBody] AddBoardColumnRequestDto request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _teamService.UpdateBoardColumnAsync(id, boardId, columnId, request.Title, userId);
        return result.ToActionResult();
    }

    [HttpPut("{id:int}/boards/{boardId:int}/columns/reorder")]
    public async Task<IActionResult> ReorderColumns(int id, int boardId, [FromBody] ReorderBoardColumnsRequestDto request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _teamService.ReorderBoardColumnsAsync(id, boardId, request.ColumnIds, userId);
        return result.ToActionResult();
    }

    [HttpPost("{id:int}/boards/{boardId:int}/tasks")]
    public async Task<IActionResult> CreateBoardTask(int id, int boardId, [FromBody] CreateTeamTaskRequestDto request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var task = new TaskItem
        {
            Title = request.Title,
            Description = request.Description,
            CategoryId = request.CategoryId,
            Priority = request.Priority,
            StartDate = request.StartDate,
            DueDate = request.DueDate
        };

        var result = await _taskService.CreateTeamTaskAsync(
            task,
            id,
            boardId,
            request.BoardColumnId,
            request.AssignedToUserId,
            userId);

        return result.ToActionResult(createdTask => Ok(createdTask));
    }

    [HttpPost("{id:int}/members")]
    public async Task<IActionResult> AddMember(int id, [FromBody] AddTeamMemberRequestDto request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _teamService.AddMemberAsync(id, request.Email, userId);
        return result.ToActionResult(() => Ok(new { success = true, message = "Uye eklendi." }));
    }

    [HttpDelete("{id:int}/members/{memberUserId:int}")]
    public async Task<IActionResult> RemoveMember(int id, int memberUserId)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _teamService.RemoveMemberAsync(id, memberUserId, userId);
        return result.ToActionResult();
    }

    [HttpGet("{id:int}/activity")]
    public async Task<IActionResult> GetActivity(int id)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _teamService.GetTeamActivityAsync(id, userId);
        return result.ToActionResult();
    }
}
