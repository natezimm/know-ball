using SkiaSharp;
using SportsTrivia.Application.Games.Silhouette;
using SportsTrivia.Infrastructure.Images;

namespace SportsTrivia.UnitTests;

public class SilhouetteRendererTests
{
    private readonly SilhouetteRenderer _renderer = new();

    /// <summary>A stand-in photo: an orange figure on a blue court.</summary>
    internal static byte[] Photo(int width = 800, int height = 600)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.SteelBlue);
            using var paint = new SKPaint { Color = SKColors.OrangeRed, IsAntialias = true };
            canvas.DrawOval(width / 2f, height / 2f, width / 6f, height / 3f, paint);
        }
        return Encode(bitmap, SKEncodedImageFormat.Jpeg);
    }

    /// <summary>The same figure with the court removed.</summary>
    internal static byte[] Cutout(int width = 800, int height = 600)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = SKColors.OrangeRed, IsAntialias = true };
            canvas.DrawOval(width / 2f, height / 2f, width / 6f, height / 3f, paint);
        }
        return Encode(bitmap, SKEncodedImageFormat.Png);
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private Task<SilhouetteImages> Render(byte[] photo, byte[] cutout) =>
        _renderer.RenderAsync(new MemoryStream(photo), new MemoryStream(cutout), CancellationToken.None);

    [Fact]
    public async Task The_silhouette_is_solid_black_where_the_subject_was_and_clear_elsewhere()
    {
        var images = await Render(Photo(), Cutout());

        using var silhouette = SKBitmap.Decode(images.Silhouette);
        Assert.Equal((800, 600), (silhouette.Width, silhouette.Height));
        Assert.Equal((byte)0, silhouette.GetPixel(5, 5).Alpha);

        var centre = silhouette.GetPixel(400, 300);
        Assert.Equal((0, 0, 0, 255), (centre.Red, centre.Green, centre.Blue, centre.Alpha));

        // Not one coloured pixel survives: the outline gives away the shape and nothing else.
        for (var y = 0; y < silhouette.Height; y += 7)
        for (var x = 0; x < silhouette.Width; x += 7)
        {
            var pixel = silhouette.GetPixel(x, y);
            Assert.Equal(0, pixel.Red + pixel.Green + pixel.Blue);
        }
    }

    [Fact]
    public async Task The_photo_is_resized_and_the_blurred_hint_is_small_and_smooth()
    {
        var images = await Render(Photo(4200, 2800), Cutout());

        using var photo = SKBitmap.Decode(images.Photo);
        Assert.Equal((SilhouetteRenderer.PhotoMaxSide, 933), (photo.Width, photo.Height));

        using var blurred = SKBitmap.Decode(images.Blurred);
        Assert.Equal((SilhouetteRenderer.BlurredMaxSide, 320), (blurred.Width, blurred.Height));

        // Just either side of the figure's left edge, a third of the way across: the photo jumps from
        // court to shirt, and the blur has barely started to change.
        static int Jump(SKBitmap b)
        {
            var y = b.Height / 2;
            return Math.Abs(b.GetPixel((int)(b.Width * 0.325), y).Blue - b.GetPixel((int)(b.Width * 0.341), y).Blue);
        }
        Assert.True(Jump(photo) > 120, $"the photo should have a hard edge, but jumped {Jump(photo)}");
        Assert.True(Jump(blurred) < 70, $"the blur should not, but jumped {Jump(blurred)}");
    }

    [Fact]
    public async Task A_cut_out_with_no_clear_background_is_refused()
    {
        var error = await Assert.ThrowsAsync<InvalidImageException>(() => Render(Photo(), Photo()));
        Assert.Contains("no clear background", error.Message);
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_refused()
    {
        var error = await Assert.ThrowsAsync<InvalidImageException>(() => Render("not an image"u8.ToArray(), Cutout()));
        Assert.StartsWith("The photo is not an image", error.Message);
    }
}
