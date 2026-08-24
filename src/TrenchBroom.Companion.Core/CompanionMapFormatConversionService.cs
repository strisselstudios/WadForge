using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TrenchBroom.Companion.Core;

public static class CompanionMapFormatConversionService
{
    private const string NumberPattern =
        @"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?";

    private static readonly Regex FacePattern =
        new(
            @"^(?<prefix>(?<indent>\s*)" +
            @"\(\s*(?<x1>" + NumberPattern + @")\s+(?<y1>" + NumberPattern + @")\s+(?<z1>" + NumberPattern + @")\s*\)\s*" +
            @"\(\s*(?<x2>" + NumberPattern + @")\s+(?<y2>" + NumberPattern + @")\s+(?<z2>" + NumberPattern + @")\s*\)\s*" +
            @"\(\s*(?<x3>" + NumberPattern + @")\s+(?<y3>" + NumberPattern + @")\s+(?<z3>" + NumberPattern + @")\s*\)\s+" +
            @"(?<texture>""[^""]*""|\S+))\s+(?<tail>.*?)\s*$",
            RegexOptions.CultureInvariant);

    private static readonly Regex StandardTailPattern =
        new(
            @"^(?<shiftX>" + NumberPattern + @")\s+" +
            @"(?<shiftY>" + NumberPattern + @")\s+" +
            @"(?<rotation>" + NumberPattern + @")\s+" +
            @"(?<scaleX>" + NumberPattern + @")\s+" +
            @"(?<scaleY>" + NumberPattern + @")" +
            @"(?<suffix>(?:\s+.*)?)$",
            RegexOptions.CultureInvariant);

    private static readonly Regex ValveTailPattern =
        new(
            @"^\[\s*" +
            NumberPattern + @"\s+" +
            NumberPattern + @"\s+" +
            NumberPattern + @"\s+" +
            NumberPattern + @"\s*\]\s+" +
            @"\[\s*" +
            NumberPattern + @"\s+" +
            NumberPattern + @"\s+" +
            NumberPattern + @"\s+" +
            NumberPattern + @"\s*\]\s+" +
            NumberPattern + @"\s+" +
            NumberPattern + @"\s+" +
            NumberPattern +
            @"(?:\s+.*)?$",
            RegexOptions.CultureInvariant);

    private static readonly TextureAxisBasis[] TextureAxisBases =
    {
        new(
            new Vector3d(0.0, 0.0, 1.0),
            new Vector3d(1.0, 0.0, 0.0),
            new Vector3d(0.0, -1.0, 0.0)),

        new(
            new Vector3d(0.0, 0.0, -1.0),
            new Vector3d(1.0, 0.0, 0.0),
            new Vector3d(0.0, -1.0, 0.0)),

        new(
            new Vector3d(1.0, 0.0, 0.0),
            new Vector3d(0.0, 1.0, 0.0),
            new Vector3d(0.0, 0.0, -1.0)),

        new(
            new Vector3d(-1.0, 0.0, 0.0),
            new Vector3d(0.0, 1.0, 0.0),
            new Vector3d(0.0, 0.0, -1.0)),

        new(
            new Vector3d(0.0, 1.0, 0.0),
            new Vector3d(1.0, 0.0, 0.0),
            new Vector3d(0.0, 0.0, -1.0)),

        new(
            new Vector3d(0.0, -1.0, 0.0),
            new Vector3d(1.0, 0.0, 0.0),
            new Vector3d(0.0, 0.0, -1.0))
    };

    public static bool EnsureValve220(
        string mapPath)
    {
        if (string.IsNullOrWhiteSpace(
                mapPath))
        {
            throw new ArgumentException(
                "A map path is required.",
                nameof(mapPath));
        }

        string fullMapPath =
            Path.GetFullPath(
                mapPath);

        if (!File.Exists(
                fullMapPath))
        {
            throw new FileNotFoundException(
                "The map file does not exist.",
                fullMapPath);
        }

        byte[] sourceBytes =
            File.ReadAllBytes(
                fullMapPath);

        bool hasUtf8Bom =
            HasUtf8Bom(
                sourceBytes);

        int textOffset =
            hasUtf8Bom
                ? 3
                : 0;

        string sourceText;

        try
        {
            sourceText =
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true)
                    .GetString(
                        sourceBytes,
                        textOffset,
                        sourceBytes.Length -
                        textOffset);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                $"Map '{Path.GetFileName(fullMapPath)}' is not valid UTF-8/ASCII text. " +
                "Companion will not rewrite its brush syntax automatically.",
                exception);
        }

        ConversionPass pass =
            ConvertStandardFaces(
                sourceText,
                fullMapPath);

        if (pass.StandardFaceCount > 0 &&
            pass.ValveFaceCount > 0)
        {
            throw new InvalidDataException(
                $"Map '{Path.GetFileName(fullMapPath)}' mixes Standard and Valve 220 brush-face syntax. " +
                "Companion will not guess how to rewrite a mixed-format map.");
        }

        if (pass.StandardFaceCount == 0)
        {
            return false;
        }

        WriteUtf8TextAtomically(
            fullMapPath,
            pass.ConvertedText,
            hasUtf8Bom);

        return true;
    }

    private static ConversionPass ConvertStandardFaces(
        string sourceText,
        string mapPath)
    {
        StringBuilder output =
            new(
                sourceText.Length +
                1024);

        int standardFaceCount =
            0;

        int valveFaceCount =
            0;

        int lineNumber =
            1;

        int offset =
            0;

        while (offset <
               sourceText.Length)
        {
            int lineEnd =
                offset;

            while (lineEnd <
                       sourceText.Length &&
                   sourceText[lineEnd] !=
                       '\r' &&
                   sourceText[lineEnd] !=
                       '\n')
            {
                lineEnd++;
            }

            string line =
                sourceText.Substring(
                    offset,
                    lineEnd -
                    offset);

            string ending =
                string.Empty;

            if (lineEnd <
                sourceText.Length)
            {
                if (sourceText[lineEnd] ==
                        '\r' &&
                    lineEnd + 1 <
                        sourceText.Length &&
                    sourceText[lineEnd + 1] ==
                        '\n')
                {
                    ending =
                        "\r\n";

                    lineEnd +=
                        2;
                }
                else
                {
                    ending =
                        sourceText[lineEnd]
                            .ToString();

                    lineEnd++;
                }
            }

            FaceKind kind =
                ClassifyFace(
                    line,
                    mapPath,
                    lineNumber,
                    out Match? faceMatch,
                    out Match? standardTailMatch);

            switch (kind)
            {
                case FaceKind.Standard:
                    standardFaceCount++;

                    output.Append(
                        ConvertStandardFace(
                            faceMatch!,
                            standardTailMatch!,
                            mapPath,
                            lineNumber));
                    break;

                case FaceKind.Valve:
                    valveFaceCount++;

                    output.Append(
                        line);
                    break;

                default:
                    output.Append(
                        line);
                    break;
            }

            output.Append(
                ending);

            offset =
                lineEnd;

            lineNumber++;
        }

        return new ConversionPass(
            output.ToString(),
            standardFaceCount,
            valveFaceCount);
    }

    private static FaceKind ClassifyFace(
        string line,
        string mapPath,
        int lineNumber,
        out Match? faceMatch,
        out Match? standardTailMatch)
    {
        faceMatch =
            FacePattern.Match(
                line);

        standardTailMatch =
            null;

        if (!faceMatch.Success)
        {
            if (line.TrimStart()
                    .StartsWith(
                        "(",
                        StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Map '{Path.GetFileName(mapPath)}' contains unsupported brush-face syntax at line {lineNumber}. " +
                    "The project copy was not converted.");
            }

            return FaceKind.NotFace;
        }

        string tail =
            faceMatch.Groups["tail"]
                .Value;

        if (ValveTailPattern.IsMatch(
                tail))
        {
            return FaceKind.Valve;
        }

        standardTailMatch =
            StandardTailPattern.Match(
                tail);

        if (standardTailMatch.Success)
        {
            return FaceKind.Standard;
        }

        throw new InvalidDataException(
            $"Map '{Path.GetFileName(mapPath)}' contains unrecognized texture projection syntax at line {lineNumber}. " +
            "The project copy was not converted.");
    }

    private static string ConvertStandardFace(
        Match face,
        Match tail,
        string mapPath,
        int lineNumber)
    {
        Vector3d p0 =
            ReadPoint(
                face,
                "x1",
                "y1",
                "z1",
                mapPath,
                lineNumber);

        Vector3d p1 =
            ReadPoint(
                face,
                "x2",
                "y2",
                "z2",
                mapPath,
                lineNumber);

        Vector3d p2 =
            ReadPoint(
                face,
                "x3",
                "y3",
                "z3",
                mapPath,
                lineNumber);

        Vector3d normal =
            Cross(
                Subtract(
                    p0,
                    p1),
                Subtract(
                    p2,
                    p1));

        double normalLength =
            Length(
                normal);

        if (normalLength <=
            1.0e-12)
        {
            throw new InvalidDataException(
                $"Map '{Path.GetFileName(mapPath)}' contains a degenerate brush plane at line {lineNumber}. " +
                "The project copy was not converted.");
        }

        normal =
            Scale(
                normal,
                1.0 /
                normalLength);

        TextureAxisBasis basis =
            FindTextureAxisBasis(
                normal);

        double rotation =
            ParseNumber(
                tail.Groups["rotation"]
                    .Value,
                mapPath,
                lineNumber);

        Vector3d uAxis =
            basis.U;

        Vector3d vAxis =
            basis.V;

        RotateTextureAxes(
            ref uAxis,
            ref vAxis,
            rotation);

        double scaleX =
            ParseNumber(
                tail.Groups["scaleX"]
                    .Value,
                mapPath,
                lineNumber);

        double scaleY =
            ParseNumber(
                tail.Groups["scaleY"]
                    .Value,
                mapPath,
                lineNumber);

        string scaleXText =
            Math.Abs(
                scaleX) <=
                1.0e-12
                ? "1"
                : tail.Groups["scaleX"]
                    .Value;

        string scaleYText =
            Math.Abs(
                scaleY) <=
                1.0e-12
                ? "1"
                : tail.Groups["scaleY"]
                    .Value;

        return
            face.Groups["prefix"]
                .Value +
            " [ " +
            FormatAxisNumber(
                uAxis.X) +
            " " +
            FormatAxisNumber(
                uAxis.Y) +
            " " +
            FormatAxisNumber(
                uAxis.Z) +
            " " +
            tail.Groups["shiftX"]
                .Value +
            " ] [ " +
            FormatAxisNumber(
                vAxis.X) +
            " " +
            FormatAxisNumber(
                vAxis.Y) +
            " " +
            FormatAxisNumber(
                vAxis.Z) +
            " " +
            tail.Groups["shiftY"]
                .Value +
            " ] 0 " +
            scaleXText +
            " " +
            scaleYText +
            tail.Groups["suffix"]
                .Value;
    }

    private static Vector3d ReadPoint(
        Match face,
        string xGroup,
        string yGroup,
        string zGroup,
        string mapPath,
        int lineNumber)
    {
        return new Vector3d(
            ParseNumber(
                face.Groups[xGroup]
                    .Value,
                mapPath,
                lineNumber),
            ParseNumber(
                face.Groups[yGroup]
                    .Value,
                mapPath,
                lineNumber),
            ParseNumber(
                face.Groups[zGroup]
                    .Value,
                mapPath,
                lineNumber));
    }

    private static double ParseNumber(
        string value,
        string mapPath,
        int lineNumber)
    {
        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double result) ||
            double.IsNaN(
                result) ||
            double.IsInfinity(
                result))
        {
            throw new InvalidDataException(
                $"Map '{Path.GetFileName(mapPath)}' contains an invalid numeric value at line {lineNumber}: '{value}'.");
        }

        return result;
    }

    private static TextureAxisBasis FindTextureAxisBasis(
        Vector3d normal)
    {
        double best =
            0.0;

        TextureAxisBasis bestBasis =
            TextureAxisBases[0];

        foreach (TextureAxisBasis basis in
                 TextureAxisBases)
        {
            double dot =
                Dot(
                    normal,
                    basis.Normal);

            if (dot >
                best)
            {
                best =
                    dot;

                bestBasis =
                    basis;
            }
        }

        return bestBasis;
    }

    private static void RotateTextureAxes(
        ref Vector3d uAxis,
        ref Vector3d vAxis,
        double rotationDegrees)
    {
        double radians =
            rotationDegrees /
            180.0 *
            Math.PI;

        double sine =
            Math.Sin(
                radians);

        double cosine =
            Math.Cos(
                radians);

        int sVector =
            FindNonZeroComponent(
                uAxis);

        int tVector =
            FindNonZeroComponent(
                vAxis);

        uAxis =
            RotateAxis(
                uAxis,
                sVector,
                tVector,
                sine,
                cosine);

        vAxis =
            RotateAxis(
                vAxis,
                sVector,
                tVector,
                sine,
                cosine);
    }

    private static Vector3d RotateAxis(
        Vector3d axis,
        int sVector,
        int tVector,
        double sine,
        double cosine)
    {
        double[] values =
        {
            axis.X,
            axis.Y,
            axis.Z
        };

        double newS =
            cosine *
                values[sVector] -
            sine *
                values[tVector];

        double newT =
            sine *
                values[sVector] +
            cosine *
                values[tVector];

        values[sVector] =
            newS;

        values[tVector] =
            newT;

        return new Vector3d(
            values[0],
            values[1],
            values[2]);
    }

    private static int FindNonZeroComponent(
        Vector3d axis)
    {
        if (axis.X !=
            0.0)
        {
            return 0;
        }

        if (axis.Y !=
            0.0)
        {
            return 1;
        }

        return 2;
    }

    private static string FormatAxisNumber(
        double value)
    {
        if (Math.Abs(
                value) <
            1.0e-12)
        {
            value =
                0.0;
        }

        return value.ToString(
            "0.###############",
            CultureInfo.InvariantCulture);
    }

    private static Vector3d Subtract(
        Vector3d left,
        Vector3d right)
    {
        return new Vector3d(
            left.X -
                right.X,
            left.Y -
                right.Y,
            left.Z -
                right.Z);
    }

    private static Vector3d Scale(
        Vector3d value,
        double factor)
    {
        return new Vector3d(
            value.X *
                factor,
            value.Y *
                factor,
            value.Z *
                factor);
    }

    private static Vector3d Cross(
        Vector3d left,
        Vector3d right)
    {
        return new Vector3d(
            left.Y *
                right.Z -
            left.Z *
                right.Y,
            left.Z *
                right.X -
            left.X *
                right.Z,
            left.X *
                right.Y -
            left.Y *
                right.X);
    }

    private static double Dot(
        Vector3d left,
        Vector3d right)
    {
        return
            left.X *
                right.X +
            left.Y *
                right.Y +
            left.Z *
                right.Z;
    }

    private static double Length(
        Vector3d value)
    {
        return Math.Sqrt(
            Dot(
                value,
                value));
    }

    private static bool HasUtf8Bom(
        byte[] sourceBytes)
    {
        return
            sourceBytes.Length >= 3 &&
            sourceBytes[0] == 0xEF &&
            sourceBytes[1] == 0xBB &&
            sourceBytes[2] == 0xBF;
    }

    private static void WriteUtf8TextAtomically(
        string mapPath,
        string text,
        bool emitUtf8Bom)
    {
        string temporaryPath =
            mapPath +
            ".tbcompanion-format-" +
            Guid.NewGuid().ToString(
                "N");

        try
        {
            using (
                FileStream output =
                    new(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None))
            using (
                StreamWriter writer =
                    new(
                        output,
                        new UTF8Encoding(
                            emitUtf8Bom),
                        bufferSize: 4096,
                        leaveOpen: false))
            {
                writer.Write(
                    text);

                writer.Flush();

                output.Flush(
                    flushToDisk: true);
            }

            File.Move(
                temporaryPath,
                mapPath,
                overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(
                        temporaryPath))
                {
                    File.Delete(
                        temporaryPath);
                }
            }
            catch
            {
                // Preserve the original conversion result.
            }
        }
    }

    private enum FaceKind
    {
        NotFace,
        Standard,
        Valve
    }

    private readonly record struct ConversionPass(
        string ConvertedText,
        int StandardFaceCount,
        int ValveFaceCount);

    private readonly record struct TextureAxisBasis(
        Vector3d Normal,
        Vector3d U,
        Vector3d V);

    private readonly record struct Vector3d(
        double X,
        double Y,
        double Z);
}
