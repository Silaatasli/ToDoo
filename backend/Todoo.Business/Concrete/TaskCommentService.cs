using System.Text.RegularExpressions;
using Todoo.Business.Abstract;
using Todoo.Business.Models;
using Todoo.Business.Models.Teams;
using Todoo.DataAccess.UnitOfWork;
using Todoo.Entities.Entities;
using Todoo.Entities.Enums;

namespace Todoo.Business.Concrete;

public class TaskCommentService : ITaskCommentService
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

    public TaskCommentService(
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

    public async Task<ServiceResult<IEnumerable<TaskCommentDto>>> ListAsync(int taskId, int userId) // thread yapısı
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult<IEnumerable<TaskCommentDto>>.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var users = (await _unitOfWork.Users.GetAllAsync()).ToDictionary(user => user.Id, user => user.Email);
        var comments = (await _unitOfWork.TaskComments.GetAllAsync())
            .Where(comment => comment.TaskId == taskId)
            .ToList();
        var attachments = (await _unitOfWork.CommentAttachments.GetAllAsync())
            .Where(attachment => comments.Any(comment => comment.Id == attachment.CommentId))
            .ToList();

        var attachmentLookup = attachments
            .GroupBy(attachment => attachment.CommentId)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.CreatedDate).ToList()); 

        var tree = comments
            .Where(comment => comment.ParentCommentId is null)
            .OrderBy(comment => comment.CreatedDate)
            .Select(comment => MapToDto(comment, comments, attachmentLookup, users))
            .ToList();

        return ServiceResult<IEnumerable<TaskCommentDto>>.Ok(tree); // thread yapısı
    }

    public async Task<ServiceResult<TaskCommentDto>> CreateAsync( // yeni yorum ekleme
        int taskId,
        string body,
        int? parentCommentId,
        int userId)
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult<TaskCommentDto>.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var trimmedBody = body.Trim();
        if (trimmedBody.Length > 4000)
        {
            return ServiceResult<TaskCommentDto>.Fail("Yorum en fazla 4000 karakter olabilir.");
        }

        if (parentCommentId.HasValue)
        {
            var parent = await _unitOfWork.TaskComments.GetByIdAsync(parentCommentId.Value);
            if (parent is null || parent.TaskId != taskId)
            {
                return ServiceResult<TaskCommentDto>.Fail("Yanit verilecek yorum bulunamadi.", ServiceErrorKind.NotFound);
            }
        }

        var task = taskResult.Data!;
        var comment = new TaskComment
        {
            TaskId = taskId,
            AuthorUserId = userId,
            ParentCommentId = parentCommentId,
            Body = trimmedBody
        };

        _unitOfWork.TaskComments.Add(comment);
        await _unitOfWork.SaveChangesAsync();

        var preview = trimmedBody.Length > 120 ? $"{trimmedBody[..120]}..." : trimmedBody;
        await LogActivityAsync(task.TeamId, task.Id, userId, TaskActivityAction.CommentAdded, null, preview);
        await _boardNotifier.NotifyBoardChangedAsync(task.TeamId, TeamBoardChangeTypes.TaskUpdated, userId, task.Id);

        var author = await _unitOfWork.Users.GetByIdAsync(userId);
        return ServiceResult<TaskCommentDto>.Ok(MapToDto(comment, author?.Email ?? string.Empty));
    }

    public async Task<ServiceResult> DeleteAsync(int taskId, int commentId, int userId)
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var comment = await _unitOfWork.TaskComments.GetByIdAsync(commentId);
        if (comment is null || comment.TaskId != taskId) 
        {
            return ServiceResult.Fail("Yorum bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (!await CanManageCommentAsync(comment, taskResult.Data!, userId))
        {
            return ServiceResult.Fail("Bu yorumu silme yetkiniz yok.", ServiceErrorKind.Forbidden);
        }

        var allComments = (await _unitOfWork.TaskComments.GetAllAsync())
            .Where(item => item.TaskId == taskId)
            .ToList();
        var descendants = CollectDescendants(commentId, allComments); 
        var commentIds = descendants.Select(item => item.Id).Append(commentId).ToHashSet(); // Silinecek yorum ve alt yorumların Id'lerini bir HashSet'e ekliyoruz.

        var attachments = (await _unitOfWork.CommentAttachments.GetAllAsync())
            .Where(attachment => commentIds.Contains(attachment.CommentId))
            .ToList();

        foreach (var attachment in attachments)
        {
            await DeleteLinkedTaskAttachmentsAsync(taskId, attachment.ObjectKey);
            await _unitOfWork.CommentAttachments.DeleteAsync(attachment.Id);
            await DeleteStoredObjectIfOrphanedAsync(attachment.ObjectKey);
        }

        foreach (var descendant in descendants.OrderByDescending(item => item.Id))
        {
            await _unitOfWork.TaskComments.DeleteAsync(descendant.Id);
        }

        await _unitOfWork.TaskComments.DeleteAsync(commentId);
        await _unitOfWork.SaveChangesAsync();

        var task = taskResult.Data!;
        var preview = comment.Body.Length > 120 ? $"{comment.Body[..120]}..." : comment.Body;
        await LogActivityAsync(task.TeamId, task.Id, userId, TaskActivityAction.CommentDeleted, preview, null);
        await _boardNotifier.NotifyBoardChangedAsync(task.TeamId, TeamBoardChangeTypes.TaskUpdated, userId, task.Id);

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<CommentAttachmentDto>> UploadAttachmentAsync(
        int taskId,
        int commentId,
        string fileName,
        string contentType,
        long sizeBytes,
        Stream fileStream,
        int userId)
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult<CommentAttachmentDto>.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var comment = await _unitOfWork.TaskComments.GetByIdAsync(commentId);
        if (comment is null || comment.TaskId != taskId)
        {
            return ServiceResult<CommentAttachmentDto>.Fail("Yorum bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (sizeBytes <= 0)
        {
            return ServiceResult<CommentAttachmentDto>.Fail("Bos dosya yuklenemez.");
        }

        if (sizeBytes > MaxFileSizeBytes)
        {
            return ServiceResult<CommentAttachmentDto>.Fail("Dosya boyutu en fazla 10 MB olabilir.");
        }

        var normalizedContentType = ResolveContentType(contentType, fileName);
        if (!AllowedContentTypes.Contains(normalizedContentType))
        {
            return ServiceResult<CommentAttachmentDto>.Fail("Desteklenmeyen dosya tipi. JPG, PNG, WEBP, GIF veya PDF yukleyin.");
        }

        var safeFileName = SanitizeFileName(fileName); // Dosya adını güvenli hale getiriyoruz (geçersiz karakterleri kaldırıyoruz ve uzunluğu sınırlıyoruz).
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            return ServiceResult<CommentAttachmentDto>.Fail("Gecersiz dosya adi.");
        }

        var task = taskResult.Data!; 
        var objectKey = $"teams/{task.TeamId}/tasks/{task.Id}/{Guid.NewGuid():N}-{safeFileName}";

        try
        {
            await _fileStorage.UploadAsync(objectKey, fileStream, sizeBytes, normalizedContentType);
        }
        catch (InvalidOperationException ex)
        {
            return ServiceResult<CommentAttachmentDto>.Fail(ex.Message, ServiceErrorKind.Validation);
        }

        var attachment = new CommentAttachment
        {
            CommentId = commentId,
            UploadedByUserId = userId,
            FileName = safeFileName,
            ContentType = normalizedContentType,
            SizeBytes = sizeBytes,
            ObjectKey = objectKey
        };

        _unitOfWork.CommentAttachments.Add(attachment);
        _unitOfWork.TaskAttachments.Add(new TaskAttachment
        {
            TaskId = taskId,
            UploadedByUserId = userId,
            FileName = safeFileName,
            ContentType = normalizedContentType,
            SizeBytes = sizeBytes,
            ObjectKey = objectKey
        });
        await _unitOfWork.SaveChangesAsync();

        await LogActivityAsync(task.TeamId, task.Id, userId, TaskActivityAction.AttachmentAdded, null, safeFileName); 
        await _boardNotifier.NotifyBoardChangedAsync(task.TeamId, TeamBoardChangeTypes.TaskUpdated, userId, task.Id); 

        var uploader = await _unitOfWork.Users.GetByIdAsync(userId);
        return ServiceResult<CommentAttachmentDto>.Ok(MapToDto(attachment, uploader?.Email ?? string.Empty));
    }

    public async Task<ServiceResult<(Stream Stream, string ContentType, string FileName)>> DownloadAttachmentAsync(
        int taskId,
        int commentId,
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

        var comment = await _unitOfWork.TaskComments.GetByIdAsync(commentId);
        if (comment is null || comment.TaskId != taskId)
        {
            return ServiceResult<(Stream, string, string)>.Fail("Yorum bulunamadi.", ServiceErrorKind.NotFound);
        }

        var attachment = await _unitOfWork.CommentAttachments.GetByIdAsync(attachmentId);
        if (attachment is null || attachment.CommentId != commentId)
        {
            return ServiceResult<(Stream, string, string)>.Fail("Dosya bulunamadi.", ServiceErrorKind.NotFound);
        }

        var stream = await _fileStorage.DownloadAsync(attachment.ObjectKey);
        return ServiceResult<(Stream, string, string)>.Ok((stream, attachment.ContentType, attachment.FileName));
    }

    public async Task<ServiceResult> DeleteAttachmentAsync(
        int taskId,
        int commentId,
        int attachmentId,
        int userId)
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var comment = await _unitOfWork.TaskComments.GetByIdAsync(commentId);
        if (comment is null || comment.TaskId != taskId)
        {
            return ServiceResult.Fail("Yorum bulunamadi.", ServiceErrorKind.NotFound);
        }

        var attachment = await _unitOfWork.CommentAttachments.GetByIdAsync(attachmentId);
        if (attachment is null || attachment.CommentId != commentId)
        {
            return ServiceResult.Fail("Dosya bulunamadi.", ServiceErrorKind.NotFound);
        }

        var task = taskResult.Data!;
        var canDelete = attachment.UploadedByUserId == userId //o commenti yazan kişi ya ya da taskı oluşturan kişi silebilir
            || comment.AuthorUserId == userId
            || task.CreatedByUserId == userId;
        var team = await _unitOfWork.Teams.GetByIdAsync(task.TeamId);
        if (team?.LeaderUserId == userId)
        {
            canDelete = true;
        }

        if (!canDelete)
        {
            return ServiceResult.Fail("Bu dosyayi silme yetkiniz yok.", ServiceErrorKind.Forbidden);
        }

        await DeleteLinkedTaskAttachmentsAsync(taskId, attachment.ObjectKey);
        await _unitOfWork.CommentAttachments.DeleteAsync(attachment.Id);
        await _unitOfWork.SaveChangesAsync();

        await DeleteStoredObjectIfOrphanedAsync(attachment.ObjectKey);

        await LogActivityAsync(task.TeamId, task.Id, userId, TaskActivityAction.AttachmentDeleted, attachment.FileName, null);
        await _boardNotifier.NotifyBoardChangedAsync(task.TeamId, TeamBoardChangeTypes.TaskUpdated, userId, task.Id);

        return ServiceResult.Ok();
    }

    private async Task DeleteLinkedTaskAttachmentsAsync(int taskId, string objectKey)
    {
        var linkedTaskAttachments = (await _unitOfWork.TaskAttachments.GetAllAsync())
            .Where(attachment => attachment.TaskId == taskId && attachment.ObjectKey == objectKey)
            .ToList();

        foreach (var linkedAttachment in linkedTaskAttachments)
        {
            await _unitOfWork.TaskAttachments.DeleteAsync(linkedAttachment.Id);
        }
    }

    private async Task DeleteStoredObjectIfOrphanedAsync(string objectKey) // Eğer başka bir yorum veya görevde kullanılmıyorsa dosyayı sil
    {
        var stillUsedByTask = (await _unitOfWork.TaskAttachments.GetAllAsync())
            .Any(attachment => attachment.ObjectKey == objectKey);
        if (stillUsedByTask)
        {
            return;
        }

        var stillUsedByComment = (await _unitOfWork.CommentAttachments.GetAllAsync())
            .Any(attachment => attachment.ObjectKey == objectKey);
        if (stillUsedByComment)
        {
            return;
        }

        await _fileStorage.DeleteAsync(objectKey);
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

    private async Task<bool> CanManageCommentAsync(TaskComment comment, TaskItem task, int userId)
    {
        if (comment.AuthorUserId == userId || task.CreatedByUserId == userId)
        {
            return true;
        }

        var team = await _unitOfWork.Teams.GetByIdAsync(task.TeamId);
        return team?.LeaderUserId == userId;
    }

    private static List<TaskComment> CollectDescendants(int rootId, IReadOnlyCollection<TaskComment> allComments)
    {
        var result = new List<TaskComment>();
        var queue = new Queue<int>();
        queue.Enqueue(rootId);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            var children = allComments.Where(comment => comment.ParentCommentId == currentId).ToList();
            foreach (var child in children)
            {
                result.Add(child);
                queue.Enqueue(child.Id);
            }
        }

        return result;
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

    private static TaskCommentDto MapToDto(
        TaskComment comment,
        IReadOnlyCollection<TaskComment> allComments,
        IReadOnlyDictionary<int, List<CommentAttachment>> attachmentLookup,
        IReadOnlyDictionary<int, string> userEmails)
    {
        var attachments = attachmentLookup.GetValueOrDefault(comment.Id, []);
        var replies = allComments
            .Where(item => item.ParentCommentId == comment.Id)
            .OrderBy(item => item.CreatedDate)
            .Select(item => MapToDto(item, allComments, attachmentLookup, userEmails))
            .ToList();

        return new TaskCommentDto
        {
            Id = comment.Id,
            TaskId = comment.TaskId,
            ParentCommentId = comment.ParentCommentId,
            Body = comment.Body,
            AuthorUserId = comment.AuthorUserId,
            AuthorEmail = userEmails.GetValueOrDefault(comment.AuthorUserId, string.Empty),
            CreatedDate = comment.CreatedDate,
            Attachments = attachments.Select(attachment => MapToDto(attachment, userEmails)).ToList(),
            Replies = replies
        };
    }

    private static TaskCommentDto MapToDto(TaskComment comment, string authorEmail)
    {
        return new TaskCommentDto
        {
            Id = comment.Id,
            TaskId = comment.TaskId,
            ParentCommentId = comment.ParentCommentId,
            Body = comment.Body,
            AuthorUserId = comment.AuthorUserId,
            AuthorEmail = authorEmail,
            CreatedDate = comment.CreatedDate,
            Attachments = [],
            Replies = []
        };
    }

    private static CommentAttachmentDto MapToDto(CommentAttachment attachment, IReadOnlyDictionary<int, string> userEmails)
    {
        return new CommentAttachmentDto
        {
            Id = attachment.Id,
            CommentId = attachment.CommentId,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            SizeBytes = attachment.SizeBytes,
            UploadedByUserId = attachment.UploadedByUserId,
            UploadedByEmail = userEmails.GetValueOrDefault(attachment.UploadedByUserId, string.Empty),
            CreatedDate = attachment.CreatedDate
        };
    }

    private static CommentAttachmentDto MapToDto(CommentAttachment attachment, string uploadedByEmail)
    {
        return new CommentAttachmentDto
        {
            Id = attachment.Id,
            CommentId = attachment.CommentId,
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
