using SkiaSharp;
using KnowBall.Application.Games.Silhouette;

namespace KnowBall.Infrastructure.Images;

/// <summary>
/// Makes a puzzle's three images. The silhouette comes from a cut-out of the subject (the photo
/// with its background removed): every pixel is painted black and only its transparency is kept,
/// so nothing of the photo itself survives in it.
/// </summary>
public sealed class SilhouetteRenderer : ISilhouetteRenderer
{
    public const int PhotoMaxSide = 1400;
    public const int SilhouetteMaxSide = 900;
    public const int BlurredMaxSide = 480;
    /// <summary>The blurred hint is the photo shrunk to this size and stretched back out, which leaves shapes and colours but no faces.</summary>
    public const int BlurGridSide = 20;
    /// <summary>A cut-out needs at least this share of clear pixels, or it is just the photo again.</summary>
    public const double MinClearShare = 0.05;

    public async Task<SilhouetteImages> RenderAsync(Stream photo, Stream cutout, CancellationToken cancellationToken)
    {
        using var photoBitmap = Fit(await LoadAsync(photo, "The photo", cancellationToken), PhotoMaxSide);
        using var cutoutBitmap = Fit(await LoadAsync(cutout, "The cut-out", cancellationToken), SilhouetteMaxSide);

        using var grid = Fit(photoBitmap.Copy(), BlurGridSide);
        using var blurred = Resize(grid, Scale(photoBitmap.Width, photoBitmap.Height, BlurredMaxSide), SKCubicResampler.Mitchell);

        var clear = 0L;
        var pixels = cutoutBitmap.GetPixelSpan();
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (pixels[i + 3] < 128) clear++;
            pixels[i] = pixels[i + 1] = pixels[i + 2] = 0;
        }

        if (clear < (long)cutoutBitmap.Width * cutoutBitmap.Height * MinClearShare)
            throw new InvalidImageException("The cut-out has no clear background. Upload a PNG of just the player, with the background removed.");

        return new SilhouetteImages(
            Encode(photoBitmap, SKEncodedImageFormat.Jpeg, 85),
            Encode(cutoutBitmap, SKEncodedImageFormat.Png, 100),
            Encode(blurred, SKEncodedImageFormat.Jpeg, 75));
    }

    /// <summary>Decodes an upload into upright RGBA pixels.</summary>
    private static async Task<SKBitmap> LoadAsync(Stream stream, string what, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        using var data = SKData.CreateCopy(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        using var codec = SKCodec.Create(data);
        if (codec is null) throw new InvalidImageException($"{what} is not an image this site can read. Use a JPEG or PNG.");

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            throw new InvalidImageException($"{what} could not be read. Try saving it again as a JPEG or PNG.");
        }

        return Upright(bitmap, codec.EncodedOrigin);
    }

    /// <summary>Phone cameras store photos sideways and note the rotation; this applies it.</summary>
    private static SKBitmap Upright(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return bitmap;

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var (w, h) = (bitmap.Width, bitmap.Height);
        var rotated = new SKBitmap(new SKImageInfo(swap ? h : w, swap ? w : h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(rotated))
        {
            canvas.Clear(SKColors.Transparent);
            switch (origin)
            {
                case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1, w / 2f, 0); break;
                case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180, w / 2f, h / 2f); break;
                case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1, 0, h / 2f); break;
                case SKEncodedOrigin.LeftTop: canvas.Translate(h, 0); canvas.RotateDegrees(90); canvas.Scale(1, -1, 0, h / 2f); break;
                case SKEncodedOrigin.RightTop: canvas.Translate(h, 0); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.RightBottom: canvas.Translate(0, w); canvas.RotateDegrees(270); canvas.Scale(1, -1, 0, h / 2f); break;
                case SKEncodedOrigin.LeftBottom: canvas.Translate(0, w); canvas.RotateDegrees(270); break;
            }
            canvas.DrawBitmap(bitmap, 0, 0);
        }

        bitmap.Dispose();
        return rotated;
    }

    /// <summary>Shrinks the bitmap so neither side exceeds <paramref name="maxSide"/>. Takes ownership of the input.</summary>
    private static SKBitmap Fit(SKBitmap bitmap, int maxSide)
    {
        if (bitmap.Width <= maxSide && bitmap.Height <= maxSide) return bitmap;
        using (bitmap) return Resize(bitmap, Scale(bitmap.Width, bitmap.Height, maxSide), SKCubicResampler.Mitchell);
    }

    private static (int Width, int Height) Scale(int width, int height, int maxSide)
    {
        var factor = Math.Min(1.0, (double)maxSide / Math.Max(width, height));
        return (Math.Max(1, (int)Math.Round(width * factor)), Math.Max(1, (int)Math.Round(height * factor)));
    }

    private static SKBitmap Resize(SKBitmap bitmap, (int Width, int Height) size, SKCubicResampler resampler) =>
        bitmap.Resize(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(resampler))
        ?? throw new InvalidImageException("The image could not be resized.");

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }
}
