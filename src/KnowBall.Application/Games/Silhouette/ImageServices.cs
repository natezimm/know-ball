namespace KnowBall.Application.Games.Silhouette;

/// <summary>Where puzzle images live: a folder in development, blob storage once deployed.</summary>
public interface IImageStore
{
    /// <summary>Stores the image under a random name and returns the address the browser loads it from.</summary>
    Task<string> SaveAsync(byte[] content, string extension, CancellationToken cancellationToken);

    Task DeleteAsync(string url, CancellationToken cancellationToken);
}

/// <summary>The three images a puzzle needs, made from the photo and a cut-out of its subject.</summary>
/// <param name="Photo">The full photo, resized. JPEG.</param>
/// <param name="Silhouette">The subject as a solid black shape on a clear background. PNG.</param>
/// <param name="Blurred">The photo, small and heavily blurred. JPEG.</param>
public sealed record SilhouetteImages(byte[] Photo, byte[] Silhouette, byte[] Blurred);

public interface ISilhouetteRenderer
{
    /// <exception cref="InvalidImageException">A file is not an image, or the cut-out has no clear background.</exception>
    Task<SilhouetteImages> RenderAsync(Stream photo, Stream cutout, CancellationToken cancellationToken);
}

/// <summary>An uploaded image cannot be used; the message is shown to the admin.</summary>
public sealed class InvalidImageException(string message) : Exception(message);
