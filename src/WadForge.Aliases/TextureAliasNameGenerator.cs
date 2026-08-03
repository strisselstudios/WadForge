using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WadForge.Aliases;

public static class TextureAliasNameGenerator
{
    public const int MaximumInternalNameLength = 16;

    private const int HashLength = 4;
    private const int SeparatorLength = 1;

    public static string CreateUnique(
        string displayName,
        ISet<string> reservedNames)
    {
        return CreateUnique(
            displayName,
            reservedNames,
            false);
    }

    public static string CreateUnique(
        string displayName,
        ISet<string> reservedNames,
        bool useTransparentPrefix)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(reservedNames);

        string sanitizedName = Sanitize(displayName);

        if (useTransparentPrefix &&
            !sanitizedName.StartsWith(
                '{'))
        {
            sanitizedName =
                "{" + sanitizedName;
        }

        if (sanitizedName.Length <=
                MaximumInternalNameLength &&
            reservedNames.Add(sanitizedName))
        {
            return sanitizedName;
        }

        int prefixLength =
            MaximumInternalNameLength -
            SeparatorLength -
            HashLength;

        string prefix = sanitizedName[
            ..Math.Min(
                prefixLength,
                sanitizedName.Length)];

        for (int collisionIndex = 0;
             collisionIndex < 100000;
             collisionIndex++)
        {
            string hashInput =
                collisionIndex == 0
                    ? displayName
                    : $"{displayName}#{collisionIndex}";

            if (useTransparentPrefix)
            {
                hashInput =
                    "transparent:" + hashInput;
            }

            string hash = CreateHash(
                hashInput);

            string candidate =
                $"{prefix}_{hash}";

            if (candidate.Length >
                MaximumInternalNameLength)
            {
                candidate = candidate[
                    ..MaximumInternalNameLength];
            }

            if (useTransparentPrefix &&
                !candidate.StartsWith(
                    '{'))
            {
                candidate = "{" + candidate;

                if (candidate.Length >
                    MaximumInternalNameLength)
                {
                    candidate = candidate[
                        ..MaximumInternalNameLength];
                }
            }

            if (reservedNames.Add(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "Unable to generate a unique internal " +
            $"texture name for '{displayName}'.");
    }

    private static string Sanitize(
        string displayName)
    {
        string normalized =
            displayName.Normalize(
                NormalizationForm.FormD);

        StringBuilder builder = new(
            normalized.Length);

        bool previousWasSeparator = false;

        foreach (char character in normalized)
        {
            UnicodeCategory category =
                CharUnicodeInfo.GetUnicodeCategory(
                    character);

            if (category ==
                UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            bool allowed =
                character <= 127 &&
                (
                    char.IsLetterOrDigit(character) ||
                    character is
                        '_' or
                        '-' or
                        '+' or
                        '{' or
                        '}' or
                        '!' or
                        '~'
                );

            if (allowed)
            {
                builder.Append(character);
                previousWasSeparator = false;
                continue;
            }

            if (!previousWasSeparator)
            {
                builder.Append('_');
                previousWasSeparator = true;
            }
        }

        string sanitized = builder
            .ToString()
            .Trim('_', '.', ' ');

        return string.IsNullOrWhiteSpace(
                sanitized)
            ? "TEXTURE"
            : sanitized;
    }

    private static string CreateHash(
        string value)
    {
        byte[] input = Encoding.UTF8.GetBytes(
            value.Normalize(
                NormalizationForm.FormKC));

        byte[] digest =
            SHA256.HashData(input);

        return Convert
            .ToHexString(digest)
            [..HashLength];
    }
}