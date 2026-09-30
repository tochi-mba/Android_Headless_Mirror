using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Rex.Mirror.Services;

/// <summary>
/// A screenshot as it is saved. The phone always sends a PNG, which is kept byte for byte; a JPG
/// is re-encoded from it, much smaller and near enough for sharing. The same decoded picture is
/// what goes on the clipboard.
/// </summary>
public static class ScreenshotFile
{
    public const int JpegQuality = 92;

    public static string Extension(string format) => format == "jpg" ? ".jpg" : ".png";

    /// <summary>The bytes to write for the chosen format.</summary>
    public static byte[] Encode(byte[] png, string format)
    {
        if (format != "jpg")
        {
            return png;
        }

        // JPG has no transparency: the picture is flattened to plain colour first.
        var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(Decode(png), PixelFormats.Bgr24, null, 0)));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    /// <summary>The picture itself, loaded completely and frozen, so any thread may use it.</summary>
    public static BitmapSource Decode(byte[] image)
    {
        using var input = new MemoryStream(image);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }
}
