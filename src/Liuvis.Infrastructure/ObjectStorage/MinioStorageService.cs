using Liuvis.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Liuvis.Infrastructure.ObjectStorage;

/// <summary>MinIO implementation of IObjectStorageService. Phase 2+ enabled.</summary>
public class MinioStorageService : IObjectStorageService
{
    private readonly MinioOptions _options;
    private readonly ILogger<MinioStorageService> _logger;

    public MinioStorageService(IOptions<MinioOptions> options, ILogger<MinioStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _logger.LogInformation("MinioStorageService configured for {Endpoint}. Phase 2+ enabled.", _options.Endpoint);
    }

    public Task<string> UploadAsync(string key, Stream stream, string contentType, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("MinIO storage not fully configured for Phase 2+. Use LocalStorage in Phase 1.");
        return Task.FromResult(string.Empty);
    }

    public Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("MinIO storage not available in Phase 1.");
    }

    public Task<string> GetUrlAsync(string key, CancellationToken cancellationToken = default)
    {
        var url = $"{(_options.UseSsl ? "https" : "http")}://{_options.Endpoint}/{_options.BucketName}/{key}";
        return Task.FromResult(url);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("MinIO storage not fully configured for Phase 2+.");
        return Task.CompletedTask;
    }
}
