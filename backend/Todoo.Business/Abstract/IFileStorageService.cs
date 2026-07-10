namespace Todoo.Business.Abstract;

public interface IFileStorageService
{
    Task EnsureBucketExistsAsync();

    Task UploadAsync(string objectKey, Stream stream, long size, string contentType);

    Task<Stream> DownloadAsync(string objectKey);

    Task DeleteAsync(string objectKey);
}
