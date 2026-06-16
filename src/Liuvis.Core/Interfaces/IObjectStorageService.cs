namespace Liuvis.Core.Interfaces;

/// <summary>Object storage abstraction for 3D model files.</summary>
public interface IObjectStorageService
{
    /// <summary>Upload a file and return its public URL.</summary>
    Task<string> UploadAsync(string key, Stream stream, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Download a file as a stream.</summary>
    Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Get the public URL for a stored object.</summary>
    Task<string> GetUrlAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Delete a stored object.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
