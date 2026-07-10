using Todoo.Business.Models;

namespace Todoo.Business.Abstract;

public interface ITaskCommentService
{
    Task<ServiceResult<IEnumerable<TaskCommentDto>>> ListAsync(int taskId, int userId);

    Task<ServiceResult<TaskCommentDto>> CreateAsync(
        int taskId,
        string body,
        int? parentCommentId,
        int userId);

    Task<ServiceResult> DeleteAsync(int taskId, int commentId, int userId);

    Task<ServiceResult<CommentAttachmentDto>> UploadAttachmentAsync(
        int taskId,
        int commentId,
        string fileName,
        string contentType,
        long sizeBytes,
        Stream fileStream,
        int userId);

    Task<ServiceResult<(Stream Stream, string ContentType, string FileName)>> DownloadAttachmentAsync(
        int taskId,
        int commentId,
        int attachmentId,
        int userId);

    Task<ServiceResult> DeleteAttachmentAsync(int taskId, int commentId, int attachmentId, int userId);
}
