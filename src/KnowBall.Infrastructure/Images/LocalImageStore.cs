using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using KnowBall.Application.Games.Silhouette;

namespace KnowBall.Infrastructure.Images;

public sealed class MediaOptions
{
    /// <summary>Folder the images are written to. Relative paths are resolved against the app's working folder.</summary>
    public string Path { get; set; } = "media";
    /// <summary>The address the folder is served from. Not "/media": the Angular build keeps its fonts there.</summary>
    public string RequestPath { get; set; } = "/photos";
}

/// <summary>
/// Keeps puzzle images in a folder that the API serves as static files. Each file gets a random
/// 128-bit name, so the address of an image that has not been released cannot be guessed.
/// </summary>
public sealed class LocalImageStore(IOptions<MediaOptions> options) : IImageStore
{
    public string Root { get; } = System.IO.Path.GetFullPath(options.Value.Path);

    public async Task<string> SaveAsync(byte[] content, string extension, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Root);
        var name = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant() + extension;
        await File.WriteAllBytesAsync(System.IO.Path.Combine(Root, name), content, cancellationToken);
        return $"{options.Value.RequestPath.TrimEnd('/')}/{name}";
    }

    public Task DeleteAsync(string url, CancellationToken cancellationToken)
    {
        // Only ever a bare file name inside the media folder.
        var name = System.IO.Path.GetFileName(url);
        if (name.Length > 0)
        {
            var path = System.IO.Path.Combine(Root, name);
            if (File.Exists(path)) File.Delete(path);
        }
        return Task.CompletedTask;
    }
}
