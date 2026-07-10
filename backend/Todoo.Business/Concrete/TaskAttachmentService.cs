using System.Text.RegularExpressions;
using Todoo.Business.Abstract;
using Todoo.Business.Models;
using Todoo.Business.Models.Teams;
using Todoo.DataAccess.UnitOfWork;
using Todoo.Entities.Entities;
using Todoo.Entities.Enums;

namespace Todoo.Business.Concrete;

public class TaskAttachmentService : ITaskAttachmentService
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
        "application/pdf"
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly ITeamService _teamService;
    private readonly IFileStorageService _fileStorage;
    private readonly ITeamBoardNotifier _boardNotifier;

    public TaskAttachmentService(
        IUnitOfWork unitOfWork,
        ITeamService teamService,
        IFileStorageService fileStorage,
        ITeamBoardNotifier boardNotifier)
    {
        _unitOfWork = unitOfWork;
        _teamService = teamService;
        _fileStorage = fileStorage;
        _boardNotifier = boardNotifier;
    }

    public async Task<ServiceResult<IEnumerable<TaskAttachmentDto>>> ListAsync(int taskId, int userId)
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult<IEnumerable<TaskAttachmentDto>>.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var users = (await _unitOfWork.Users.GetAllAsync()).ToDictionary(user => user.Id, user => user.Email);
        var attachments = (await _unitOfWork.TaskAttachments.GetAllAsync())
            .Where(attachment => attachment.TaskId == taskId)
            .OrderByDescending(attachment => attachment.CreatedDate)
            .Select(attachment => MapToDto(attachment, users))
            .ToList();

        return ServiceResult<IEnumerable<TaskAttachmentDto>>.Ok(attachments);
    }

    public async Task<ServiceResult<TaskAttachmentDto>> UploadAsync(
        int taskId,
        string fileName,
        string contentType,
        long sizeBytes,
        Stream fileStream,
        int userId)
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult<TaskAttachmentDto>.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        if (sizeBytes <= 0)
        {
            return ServiceResult<TaskAttachmentDto>.Fail("Bos dosya yuklenemez.");
        }

        if (sizeBytes > MaxFileSizeBytes)
        {
            return ServiceResult<TaskAttachmentDto>.Fail("Dosya boyutu en fazla 10 MB olabilir.");
        }

        var normalizedContentType = ResolveContentType(contentType, fileName);
        if (!AllowedContentTypes.Contains(normalizedContentType))
        {
            return ServiceResult<TaskAttachmentDto>.Fail("Desteklenmeyen dosya tipi. JPG, PNG, WEBP, GIF veya PDF yukleyin.");
        }

        var safeFileName = SanitizeFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            return ServiceResult<TaskAttachmentDto>.Fail("Gecersiz dosya adi.");
        }

        var task = taskResult.Data!;
        var objectKey = $"teams/{task.TeamId}/tasks/{task.Id}/{Guid.NewGuid():N}-{safeFileName}";

        try
        {
            await _fileStorage.UploadAsync(objectKey, fileStream, sizeBytes, normalizedContentType);
        }
        catch (InvalidOperationException ex)
        {
            return ServiceResult<TaskAttachmentDto>.Fail(ex.Message, ServiceErrorKind.Validation);
        }

        var attachment = new TaskAttachment
        {
            TaskId = taskId,
            UploadedByUserId = userId,
            FileName = safeFileName,
            ContentType = normalizedContentType,
            SizeBytes = sizeBytes,
            ObjectKey = objectKey
        };

        _unitOfWork.TaskAttachments.Add(attachment);
        await _unitOfWork.SaveChangesAsync();

        await LogActivityAsync(task.TeamId, task.Id, userId, TaskActivityAction.AttachmentAdded, null, safeFileName);
        await _boardNotifier.NotifyBoardChangedAsync(task.TeamId, TeamBoardChangeTypes.TaskUpdated, userId, task.Id);

        var uploader = await _unitOfWork.Users.GetByIdAsync(userId);
        return ServiceResult<TaskAttachmentDto>.Ok(MapToDto(attachment, uploader?.Email ?? string.Empty));
    }

    public async Task<ServiceResult<(Stream Stream, string ContentType, string FileName)>> DownloadAsync(
        int taskId,
        int attachmentId,
        int userId)
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult<(Stream, string, string)>.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var attachment = await _unitOfWork.TaskAttachments.GetByIdAsync(attachmentId);
        if (attachment is null || attachment.TaskId != taskId)
        {
            return ServiceResult<(Stream, string, string)>.Fail("Dosya bulunamadi.", ServiceErrorKind.NotFound);
        }

        var stream = await _fileStorage.DownloadAsync(attachment.ObjectKey);
        return ServiceResult<(Stream, string, string)>.Ok((stream, attachment.ContentType, attachment.FileName));
    }

    public async Task<ServiceResult> DeleteAsync(int taskId, int attachmentId, int userId)
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var attachment = await _unitOfWork.TaskAttachments.GetByIdAsync(attachmentId);
        if (attachment is null || attachment.TaskId != taskId)
        {
            return ServiceResult.Fail("Dosya bulunamadi.", ServiceErrorKind.NotFound);
        }

        var task = taskResult.Data!;
        var canDelete = attachment.UploadedByUserId == userId || task.CreatedByUserId == userId;
        var team = await _unitOfWork.Teams.GetByIdAsync(task.TeamId);
        if (team?.LeaderUserId == userId)
        {
            canDelete = true;
        }

        if (!canDelete)
        {
            return ServiceResult.Fail("Bu dosyayi silme yetkiniz yok.", ServiceErrorKind.Forbidden);
        }

        await _fileStorage.DeleteAsync(attachment.ObjectKey);
        await _unitOfWork.TaskAttachments.DeleteAsync(attachment.Id);
        await _unitOfWork.SaveChangesAsync();

        await LogActivityAsync(task.TeamId, task.Id, userId, TaskActivityAction.AttachmentDeleted, attachment.FileName, null);
        await _boardNotifier.NotifyBoardChangedAsync(task.TeamId, TeamBoardChangeTypes.TaskUpdated, userId, task.Id);

        return ServiceResult.Ok();
    }

    private async Task<ServiceResult<TaskItem>> GetTaskIfMemberAsync(int taskId, int userId)
    {
        var task = await _unitOfWork.TaskItems.GetByIdAsync(taskId);
        if (task is null)
        {
            return ServiceResult<TaskItem>.Fail("Gorev bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (!await _teamService.IsTeamMemberAsync(task.TeamId, userId))
        {
            return ServiceResult<TaskItem>.Fail("Bu gorevi gorme yetkiniz yok.", ServiceErrorKind.Forbidden);
        }

        return ServiceResult<TaskItem>.Ok(task);
    }

    private async Task LogActivityAsync(
        int teamId,
        int taskId,
        int userId,
        TaskActivityAction actionType,
        string? oldValue,
        string? newValue)
    {
        _unitOfWork.TaskActivityLogs.Add(new TaskActivityLog
        {
            TeamId = teamId,
            TaskId = taskId,
            UserId = userId,
            ActionType = actionType,
            OldValue = oldValue,
            NewValue = newValue
        });

        await _unitOfWork.SaveChangesAsync();
    }

    private static TaskAttachmentDto MapToDto(TaskAttachment attachment, IReadOnlyDictionary<int, string> userEmails)
    {
        return new TaskAttachmentDto
        {
            Id = attachment.Id,
            TaskId = attachment.TaskId,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            SizeBytes = attachment.SizeBytes,
            UploadedByUserId = attachment.UploadedByUserId,
            UploadedByEmail = userEmails.GetValueOrDefault(attachment.UploadedByUserId, string.Empty),
            CreatedDate = attachment.CreatedDate
        };
    }

    private static TaskAttachmentDto MapToDto(TaskAttachment attachment, string uploadedByEmail)
    {
        return new TaskAttachmentDto
        {
            Id = attachment.Id,
            TaskId = attachment.TaskId,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            SizeBytes = attachment.SizeBytes,
            UploadedByUserId = attachment.UploadedByUserId,
            UploadedByEmail = uploadedByEmail,
            CreatedDate = attachment.CreatedDate
        };
    }

    private static string ResolveContentType(string contentType, string fileName)
    {
        var normalized = string.IsNullOrWhiteSpace(contentType)
            ? string.Empty
            : contentType.Trim();

        if (string.Equals(normalized, "image/jpg", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "image/jpeg";
        }

        if (AllowedContentTypes.Contains(normalized))
        {
            return normalized;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".pdf" => "application/pdf",
            _ => normalized
        };
    }

    private static string SanitizeFileName(string fileName)
    {
        var trimmed = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        trimmed = Regex.Replace(trimmed, @"[^\w\.\-]", "_");
        return trimmed.Length > 180 ? trimmed[..180] : trimmed;
    }
}
