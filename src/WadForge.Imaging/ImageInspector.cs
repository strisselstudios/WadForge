using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WadForge.Imaging;

public static class ImageInspector
{
    private static readonly HashSet<string> SupportedExtensions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".tif",
        ".tiff"
    };

    public static bool IsSupportedPath(string path)
    {
        return SupportedExtensions.Contains(
            Path.GetExtension(path));
    }

    public static bool TryInspect(
        string path,
        out ImageInspectionResult? result,
        out string error)
    {
        result = null;
        error = string.Empty;

        try
        {
            if (!File.Exists(path))
            {
                error = "The image file does not exist.";
                return false;
            }

            if (!IsSupportedPath(path))
            {
                error =
                    $"Unsupported image extension: {Path.GetExtension(path)}";

                return false;
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
                error =
                    "The image contains no decodable frames.";

                return false;
            }

            BitmapFrame frame = decoder.Frames[0];

            if (frame.PixelWidth <= 0 ||
                frame.PixelHeight <= 0)
            {
                error = "The image has invalid dimensions.";
                return false;
            }

            FormatConvertedBitmap converted = new(
                frame,
                PixelFormats.Bgra32,
                null,
                0.0);

            int stride = checked(
                converted.PixelWidth * 4);

            byte[] bgraPixels = new byte[
                checked(stride * converted.PixelHeight)];

            converted.CopyPixels(
                bgraPixels,
                stride,
                0);

            bool hasTransparency = false;

            for (int index = 3;
                 index < bgraPixels.Length;
                 index += 4)
            {
                if (bgraPixels[index] < 255)
                {
                    hasTransparency = true;
                    break;
                }
            }

            string containerFormat =
                decoder.CodecInfo?.FriendlyName ??
                string.Empty;

            if (string.IsNullOrWhiteSpace(
                    containerFormat))
            {
                containerFormat = Path
                    .GetExtension(path)
                    .TrimStart('.')
                    .ToUpperInvariant();
            }

            result = new ImageInspectionResult(
                containerFormat,
                frame.PixelWidth,
                frame.PixelHeight,
                frame.Format.BitsPerPixel,
                frame.Format.ToString(),
                hasTransparency);

            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }
}