namespace Rex.Core;

/// <summary>
/// The soft background's blur, done on a small copy of the frame.
///
/// Blurring at display size is the expensive way: a shader over the whole window, every frame.
/// Capturing small, blurring small and letting the upscale do the rest costs a fraction of that
/// and looks the same, because a blurred backdrop has no detail to lose.
/// </summary>
public static class AmbientBlur
{
    /// <summary>How wide the captured copy is for a blur radius: a sharper background needs more pixels.</summary>
    public static int CaptureWidth(double blurRadius) => blurRadius switch
    {
        <= 2 => 480,
        <= 20 => 256,
        _ => 160,
    };

    /// <summary>The blur radius to apply to the small copy, given how far it was downscaled.</summary>
    public static int SmallRadius(double blurRadius, int captureWidth, int surfaceWidth)
    {
        if (blurRadius <= 0.5 || surfaceWidth <= 0 || captureWidth <= 0)
        {
            return 0;
        }

        var scaled = blurRadius * captureWidth / surfaceWidth;
        return (int)Math.Clamp(Math.Round(scaled), 1, 16);
    }

    /// <summary>
    /// Blurs a 32-bit BGRA buffer in place. Two box passes read as a Gaussian, and each pass is a
    /// running sum, so the cost is the pixel count rather than the pixel count times the radius.
    ///
    /// A caller that blurs every frame should hand in <paramref name="scratch"/>, a buffer at least
    /// as long as the picture, and keep it between frames: allocating one per frame is most of the
    /// rubbish a live background would otherwise make.
    /// </summary>
    public static void Apply(byte[] pixels, int width, int height, int radius, int passes = 2, byte[]? scratch = null)
    {
        if (radius <= 0 || width <= 0 || height <= 0 || pixels.Length < width * height * 4)
        {
            return;
        }

        if (scratch is null || scratch.Length < pixels.Length)
        {
            scratch = new byte[pixels.Length];
        }
        for (var pass = 0; pass < passes; pass++)
        {
            BoxHorizontal(pixels, scratch, width, height, radius);
            BoxVertical(scratch, pixels, width, height, radius);
        }
    }

    private static void BoxHorizontal(byte[] source, byte[] target, int width, int height, int radius)
    {
        for (var y = 0; y < height; y++)
        {
            var row = y * width * 4;
            for (var channel = 0; channel < 4; channel++)
            {
                var sum = 0;
                var count = 0;
                for (var x = 0; x <= Math.Min(radius, width - 1); x++)
                {
                    sum += source[row + x * 4 + channel];
                    count++;
                }

                for (var x = 0; x < width; x++)
                {
                    target[row + x * 4 + channel] = (byte)(sum / count);

                    var add = x + radius + 1;
                    if (add < width)
                    {
                        sum += source[row + add * 4 + channel];
                        count++;
                    }

                    var drop = x - radius;
                    if (drop >= 0)
                    {
                        sum -= source[row + drop * 4 + channel];
                        count--;
                    }
                }
            }
        }
    }

    private static void BoxVertical(byte[] source, byte[] target, int width, int height, int radius)
    {
        var stride = width * 4;
        for (var x = 0; x < width; x++)
        {
            var column = x * 4;
            for (var channel = 0; channel < 4; channel++)
            {
                var sum = 0;
                var count = 0;
                for (var y = 0; y <= Math.Min(radius, height - 1); y++)
                {
                    sum += source[y * stride + column + channel];
                    count++;
                }

                for (var y = 0; y < height; y++)
                {
                    target[y * stride + column + channel] = (byte)(sum / count);

                    var add = y + radius + 1;
                    if (add < height)
                    {
                        sum += source[add * stride + column + channel];
                        count++;
                    }

                    var drop = y - radius;
                    if (drop >= 0)
                    {
                        sum -= source[drop * stride + column + channel];
                        count--;
                    }
                }
            }
        }
    }
}
