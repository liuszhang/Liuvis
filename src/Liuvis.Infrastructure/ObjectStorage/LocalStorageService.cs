using Liuvis.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Liuvis.Infrastructure.ObjectStorage;

/// <summary>Local file system implementation of IObjectStorageService.</summary>
public class LocalStorageService : IObjectStorageService
{
    private readonly LocalStorageOptions _options;
    private readonly ILogger<LocalStorageService> _logger;
    private readonly string _basePath;

    public LocalStorageService(IOptions<LocalStorageOptions> options, ILogger<LocalStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _basePath = Path.GetFullPath(_options.BasePath);
        Directory.CreateDirectory(_basePath);
        _logger.LogInformation("LocalStorageService initialized at {BasePath}", _basePath);
    }

    public async Task<string> UploadAsync(string key, Stream stream, string contentType, CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(key);
        var directory = Path.GetDirectoryName(filePath);
        if (directory != null) Directory.CreateDirectory(directory);

        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(fileStream, cancellationToken);
        await fileStream.FlushAsync(cancellationToken);

        _logger.LogInformation("File uploaded: {Key} ({Size} bytes)", key, fileStream.Length);
        return $"/storage/{key}";
    }

    public Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(key);
        if (!File.Exists(filePath))
        {
            _logger.LogError("File not found: {Key}", key);
            throw new FileNotFoundException($"Object '{key}' not found.", filePath);
        }

        _logger.LogDebug("File stream opened: {Key}", key);
        return Task.FromResult<Stream>(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    public Task<string> GetUrlAsync(string key, CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(key);
        if (!File.Exists(filePath))
            return Task.FromResult(string.Empty);

        return Task.FromResult($"/storage/{key}");
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(key);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogInformation("File deleted: {Key}", key);
        }
        return Task.CompletedTask;
    }

    private string GetFilePath(string key)
    {
        // Sanitize key to prevent directory traversal
        var sanitizedKey = key.Replace('\\', '/').TrimStart('/');
        sanitizedKey = Path.GetInvalidFileNameChars()
            .Aggregate(sanitizedKey, (current, c) => current.Replace(c, '_'));
        return Path.Combine(_basePath, sanitizedKey);
    }
}
