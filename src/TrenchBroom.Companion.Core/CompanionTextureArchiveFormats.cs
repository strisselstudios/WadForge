using System;

namespace TrenchBroom.Companion.Core;

public static class CompanionTextureArchiveFormats
{
    public const string Wad2 = "wad2";

    public const string Wad3 = "wad3";

    public static string Normalize(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new ArgumentException(
                "Texture archive format cannot be empty.",
                nameof(value));
        }

        return value.Trim().ToLowerInvariant() switch
        {
            Wad2 => Wad2,
            Wad3 => Wad3,
            _ => throw new ArgumentException(
                $"Unsupported texture archive format '{value}'.",
                nameof(value))
        };
    }

    public static string GetDisplayName(
        string value)
    {
        return Normalize(value) switch
        {
            Wad2 => "WAD2",
            Wad3 => "WAD3",
            _ => throw new InvalidOperationException()
        };
    }
}
