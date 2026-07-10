using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using Todoo.Business.Abstract;
using Todoo.Business.Options;

namespace Todoo.Business.Concrete;

public class MinioFileStorageService : IFileStorageService
{
    private readonly IMinioClient _client;
    private readonly MinioOptions _options;
    private readonly ILogger<MinioFileStorageService> _logger;

    public MinioFileStorageService(IOptions<MinioOptions> options, ILogger<MinioFileStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;

        var (host, port) = ParseEndpoint(_options.Endpoint);
        _client = new MinioClient()
            .WithEndpoint(host, port)
            .WithCredentials(_options.AccessKey, _options.SecretKey)
            .WithSSL(_options.UseSsl)
            .Build();
    }

    public async Task EnsureBucketExistsAsync()
    {
        try
        {
            var exists = await _client.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(_options.BucketName));

            if (!exists)
            {
                await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_options.BucketName));
                _logger.LogInformation("MinIO bucket olusturuldu: {Bucket}", _options.BucketName);
            }
        }
        catch (MinioException ex)
        {
            _logger.LogError(ex, "MinIO bucket kontrolu basarisiz. Endpoint={Endpoint}", _options.Endpoint);
            throw new InvalidOperationException(
                "MinIO baglantisi kurulamadi. Erisim anahtari ve sifrenin MinIO ile eslestiginden emin olun.",
                ex);
        }
    }

    public async Task UploadAsync(string objectKey, Stream stream, long size, string contentType)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        try
        {
            await _client.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(objectKey)
                .WithStreamData(stream)
                .WithObjectSize(size)
                .WithContentType(contentType));

            _logger.LogInformation(
                "MinIO yukleme tamamlandi: {Bucket}/{ObjectKey} ({Size} bayt)",
                _options.BucketName,
                objectKey,
                size);
        }
        catch (MinioException ex)
        {
            _logger.LogError(
                ex,
                "MinIO yukleme basarisiz: {Bucket}/{ObjectKey}",
                _options.BucketName,
                objectKey);

            throw new InvalidOperationException(
                "Dosya MinIO'ya yuklenemedi. MinIO erisim bilgilerini ve servis durumunu kontrol edin.",
                ex);
        }
    }

    public async Task<Stream> DownloadAsync(string objectKey)
    {
        var memoryStream = new MemoryStream();
        try
        {
            await _client.GetObjectAsync(new GetObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(objectKey)
                .WithCallbackStream(stream => stream.CopyTo(memoryStream)));
        }
        catch (MinioException ex)
        {
            _logger.LogError(ex, "MinIO indirme basarisiz: {Bucket}/{ObjectKey}", _options.BucketName, objectKey);
            throw new InvalidOperationException("Dosya MinIO'dan okunamadi.", ex);
        }

        memoryStream.Position = 0;
        return memoryStream;
    }

    public async Task DeleteAsync(string objectKey)
    {
        try
        {
            await _client.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(objectKey));
        }
        catch (MinioException ex)
        {
            _logger.LogError(ex, "MinIO silme basarisiz: {Bucket}/{ObjectKey}", _options.BucketName, objectKey);
            throw new InvalidOperationException("Dosya MinIO'dan silinemedi.", ex);
        }
    }

    private static (string Host, int Port) ParseEndpoint(string endpoint)
    {
        var trimmed = endpoint.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["http://".Length..];
        }
        else if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["https://".Length..];
        }

        var parts = trimmed.Split(':', 2);
        var host = parts[0];
        var port = parts.Length == 2 && int.TryParse(parts[1], out var parsedPort) ? parsedPort : 9000;
        return (host, port);
    }
}
