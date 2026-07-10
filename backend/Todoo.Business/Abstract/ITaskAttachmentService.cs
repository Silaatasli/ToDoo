using Todoo.Business.Models;

namespace Todoo.Business.Abstract;

public interface ITaskAttachmentService
{
    Task<ServiceResult<IEnumerable<TaskAttachmentDto>>> ListAsync(int taskId, int userId);

    Task<ServiceResult<TaskAttachmentDto>> UploadAsync(
        int taskId,
        string fileName,
        string contentType,
        long sizeBytes,
        Stream fileStream,
        int userId);

    Task<ServiceResult<(Stream Stream, string ContentType, string FileName)>> DownloadAsync(
        int taskId,
        int attachmentId,
        int userId);

    Task<ServiceResult> DeleteAsync(int taskId, int attachmentId, int userId);
}
