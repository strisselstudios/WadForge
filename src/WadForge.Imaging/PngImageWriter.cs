using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WadForge.Imaging;

public static class PngImageWriter
{
    public static void Write(
        string path,
        RgbaImage image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(image);

        string? directory =
            Path.GetDirectoryName(path);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "The PNG path has no output directory.");
        }

        Directory.CreateDirectory(directory);

        int stride = checked(image.Width * 4);

        byte[] bgraPixels = new byte[
            checked(stride * image.Height)];

        int destinationIndex = 0;

        foreach (Rgba32 pixel in image.Pixels)
        {
            bgraPixels[destinationIndex++] = pixel.B;
            bgraPixels[destinationIndex++] = pixel.G;
            bgraPixels[destinationIndex++] = pixel.R;
            bgraPixels[destinationIndex++] = pixel.A;
        }

        BitmapSource bitmap = BitmapSource.Create(
            image.Width,
            image.Height,
            96.0,
            96.0,
            PixelFormats.Bgra32,
            null,
            bgraPixels,
            stride);

        bitmap.Freeze();

        PngBitmapEncoder encoder = new();

        encoder.Frames.Add(
            BitmapFrame.Create(bitmap));

        using FileStream stream = new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);

        encoder.Save(stream);
        stream.Flush(true);
    }
}
