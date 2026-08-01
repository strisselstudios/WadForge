namespace WadForge.Imaging;

public static class AdaptivePaletteQuantizer
{
    private const int MaximumSampleCount = 262144;

    public static Rgb24[] CreatePalette(
        RgbaImage image,
        bool reserveTransparentIndex)
    {
        ArgumentNullException.ThrowIfNull(image);

        int targetColorCount =
            reserveTransparentIndex
                ? 255
                : 256;

        Dictionary<int, int> histogram = new();

        int sampleStep = Math.Max(
            1,
            image.Pixels.Length /
            MaximumSampleCount);

        for (int index = 0;
             index < image.Pixels.Length;
             index += sampleStep)
        {
            Rgba32 pixel = image.Pixels[index];

            if (reserveTransparentIndex &&
                pixel.IsTransparent())
            {
                continue;
            }

            int packedColor =
                (pixel.R << 16) |
                (pixel.G << 8) |
                pixel.B;

            histogram.TryGetValue(
                packedColor,
                out int currentCount);

            histogram[packedColor] =
                currentCount + 1;
        }

        if (histogram.Count == 0)
        {
            histogram[0] = 1;
        }

        List<ColorPoint> points = histogram
            .Select(
                pair => new ColorPoint(
                    (byte)(pair.Key >> 16),
                    (byte)(pair.Key >> 8),
                    (byte)pair.Key,
                    pair.Value))
            .OrderBy(point => point.R)
            .ThenBy(point => point.G)
            .ThenBy(point => point.B)
            .ToList();

        List<ColorBox> boxes = new()
        {
            new ColorBox(points)
        };

        while (boxes.Count < targetColorCount)
        {
            ColorBox? selectedBox = boxes
                .Where(box => box.Points.Count > 1)
                .OrderByDescending(box => box.Score)
                .ThenByDescending(box => box.Points.Count)
                .FirstOrDefault();

            if (selectedBox is null)
            {
                break;
            }

            boxes.Remove(selectedBox);

            (ColorBox first, ColorBox second) =
                selectedBox.Split();

            boxes.Add(first);
            boxes.Add(second);
        }

        List<Rgb24> palette = boxes
            .Select(box => box.GetAverageColor())
            .ToList();

        if (palette.Count == 0)
        {
            palette.Add(new Rgb24(
                0,
                0,
                0));
        }

        while (palette.Count < targetColorCount)
        {
            palette.Add(palette[^1]);
        }

        if (palette.Count > targetColorCount)
        {
            palette.RemoveRange(
                targetColorCount,
                palette.Count - targetColorCount);
        }

        if (reserveTransparentIndex)
        {
            palette.Add(new Rgb24(
                0,
                0,
                255));
        }

        return palette.ToArray();
    }

    private readonly record struct ColorPoint(
        byte R,
        byte G,
        byte B,
        int Count);

    private sealed class ColorBox
    {
        public ColorBox(
            List<ColorPoint> points)
        {
            if (points.Count == 0)
            {
                throw new ArgumentException(
                    "A color box cannot be empty.",
                    nameof(points));
            }

            Points = points;

            int minimumRed = 255;
            int minimumGreen = 255;
            int minimumBlue = 255;

            int maximumRed = 0;
            int maximumGreen = 0;
            int maximumBlue = 0;

            long totalCount = 0;

            foreach (ColorPoint point in points)
            {
                minimumRed = Math.Min(
                    minimumRed,
                    point.R);

                minimumGreen = Math.Min(
                    minimumGreen,
                    point.G);

                minimumBlue = Math.Min(
                    minimumBlue,
                    point.B);

                maximumRed = Math.Max(
                    maximumRed,
                    point.R);

                maximumGreen = Math.Max(
                    maximumGreen,
                    point.G);

                maximumBlue = Math.Max(
                    maximumBlue,
                    point.B);

                totalCount += point.Count;
            }

            RedRange = maximumRed - minimumRed;
            GreenRange = maximumGreen - minimumGreen;
            BlueRange = maximumBlue - minimumBlue;
            TotalCount = totalCount;

            int maximumRange = Math.Max(
                RedRange,
                Math.Max(
                    GreenRange,
                    BlueRange));

            Score = checked(
                (maximumRange + 1L) *
                Math.Max(1L, totalCount));
        }

        public List<ColorPoint> Points { get; }

        public int RedRange { get; }

        public int GreenRange { get; }

        public int BlueRange { get; }

        public long TotalCount { get; }

        public long Score { get; }

        public (ColorBox First, ColorBox Second) Split()
        {
            Func<ColorPoint, byte> selector;

            if (RedRange >= GreenRange &&
                RedRange >= BlueRange)
            {
                selector = point => point.R;
            }
            else if (GreenRange >= BlueRange)
            {
                selector = point => point.G;
            }
            else
            {
                selector = point => point.B;
            }

            List<ColorPoint> sortedPoints = Points
                .OrderBy(selector)
                .ThenBy(point => point.R)
                .ThenBy(point => point.G)
                .ThenBy(point => point.B)
                .ToList();

            long halfCount = TotalCount / 2;
            long runningCount = 0;
            int splitIndex = 1;

            for (int index = 0;
                 index < sortedPoints.Count - 1;
                 index++)
            {
                runningCount +=
                    sortedPoints[index].Count;

                if (runningCount >= halfCount)
                {
                    splitIndex = index + 1;
                    break;
                }
            }

            splitIndex = Math.Clamp(
                splitIndex,
                1,
                sortedPoints.Count - 1);

            List<ColorPoint> first = sortedPoints
                .Take(splitIndex)
                .ToList();

            List<ColorPoint> second = sortedPoints
                .Skip(splitIndex)
                .ToList();

            return (
                new ColorBox(first),
                new ColorBox(second));
        }

        public Rgb24 GetAverageColor()
        {
            long redTotal = 0;
            long greenTotal = 0;
            long blueTotal = 0;
            long countTotal = 0;

            foreach (ColorPoint point in Points)
            {
                redTotal +=
                    point.R * (long)point.Count;

                greenTotal +=
                    point.G * (long)point.Count;

                blueTotal +=
                    point.B * (long)point.Count;

                countTotal += point.Count;
            }

            if (countTotal <= 0)
            {
                return new Rgb24(
                    0,
                    0,
                    0);
            }

            return new Rgb24(
                (byte)(redTotal / countTotal),
                (byte)(greenTotal / countTotal),
                (byte)(blueTotal / countTotal));
        }
    }
}
