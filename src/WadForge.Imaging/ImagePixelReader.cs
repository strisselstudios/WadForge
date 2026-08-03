using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WadForge.Imaging;

public static class ImagePixelReader
{
    public static RgbaImage Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Image file not found.",
                path);
        }

        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        BitmapDecoder decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException(
                "The image contains no decodable frames.");
        }

        BitmapFrame frame = decoder.Frames[0];

        FormatConvertedBitmap converted = new(
            frame,
            PixelFormats.Bgra32,
            null,
            0.0);

        int width = converted.PixelWidth;
        int height = converted.PixelHeight;
        int stride = checked(width * 4);

        byte[] sourcePixels = new byte[
            checked(stride * height)];

        converted.CopyPixels(
            sourcePixels,
            stride,
            0);

        Rgba32[] pixels = new Rgba32[
            checked(width * height)];

        int sourceIndex = 0;

        for (int pixelIndex = 0;
             pixelIndex < pixels.Length;
             pixelIndex++)
        {
            byte blue = sourcePixels[sourceIndex++];
            byte green = sourcePixels[sourceIndex++];
            byte red = sourcePixels[sourceIndex++];
            byte alpha = sourcePixels[sourceIndex++];

            pixels[pixelIndex] = new Rgba32(
                red,
                green,
                blue,
                alpha);
        }

        return new RgbaImage(
            width,
            height,
            pixels);
    }
}