using System;
using System.Collections.Generic;
using System.IO;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Readers;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TrenchBroom.Companion.Core;

public sealed record CompanionOnlineWadEntry(
    string SourceId,
    string SourceDisplayName,
    string FileName,
    string DisplayName,
    string ExpectedFormat,
    string PaletteHint,
    Uri SourcePageUri,
    Uri DownloadUri);

public interface ICompanionOnlineWadRepository
{
    string Id { get; }

    string DisplayName { get; }

    string Summary { get; }

    string Description { get; }

    Uri CatalogUri { get; }

    Task<IReadOnlyList<CompanionOnlineWadEntry>> GetEntriesAsync(
        CancellationToken cancellationToken);
}

public static class CompanionOnlineWadRepositories
{
    public static IReadOnlyList<ICompanionOnlineWadRepository> CreateDefault()
    {
        return new ICompanionOnlineWadRepository[]
        {
            new CompanionQuaketasticWadRepository(),
            new CompanionQuaddictedWadRepository(),
            new CompanionSlipseerWadRepository()
        };
    }
}

public sealed class CompanionQuaketasticWadRepository :
    ICompanionOnlineWadRepository
{
    private static readonly HttpClient HttpClient =
        CreateHttpClient();

    private static readonly Regex DirectWadHrefRegex =
        new(
            "href\\s*=\\s*[\"'](?<href>[^\"']+\\.wad)[\"']",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);

    public string Id =>
        "quaketastic";

    public string DisplayName =>
        "Quaketastic";

    public string Summary =>
        "Quake · WAD2 texture WADs";

    public string Description =>
        "Community Quake texture WAD archive. Companion validates the downloaded file before preview or import.";

    public Uri CatalogUri { get; } =
        new(
            "https://www.quaketastic.com/files/texture_wads/",
            UriKind.Absolute);

    public async Task<IReadOnlyList<CompanionOnlineWadEntry>> GetEntriesAsync(
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request =
            new(
                HttpMethod.Get,
                CatalogUri);

        using HttpResponseMessage response =
            await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        string html =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        Dictionary<string, CompanionOnlineWadEntry> entries =
            new(
                StringComparer.OrdinalIgnoreCase);

        foreach (Match match in
                 DirectWadHrefRegex.Matches(
                     html))
        {
            string href =
                WebUtility.HtmlDecode(
                    match.Groups["href"].Value)
                .Trim();

            if (!Uri.TryCreate(
                    CatalogUri,
                    href,
                    out Uri? downloadUri) ||
                downloadUri is null ||
                !downloadUri.AbsolutePath.EndsWith(
                    ".wad",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string fileName =
                Uri.UnescapeDataString(
                    downloadUri.Segments[^1]);

            if (string.IsNullOrWhiteSpace(
                    fileName))
            {
                continue;
            }

            CompanionOnlineWadEntry entry =
                new(
                    Id,
                    DisplayName,
                    fileName,
                    Path.GetFileNameWithoutExtension(
                        fileName),
                    "WAD2",
                    "Quake",
                    CatalogUri,
                    downloadUri);

            entries[downloadUri.AbsoluteUri] =
                entry;
        }

        return entries
            .Values
            .OrderBy(
                entry =>
                    entry.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                entry =>
                    entry.FileName,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client =
            new()
            {
                Timeout =
                    TimeSpan.FromSeconds(
                        30)
            };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "TrenchBroom-Companion/1.0");

        return client;
    }
}

public sealed class CompanionQuaddictedWadRepository :
    ICompanionOnlineWadRepository
{
    private static readonly HttpClient HttpClient =
        CreateHttpClient();

    private static readonly Regex DownloadHrefRegex =
        new(
            "href\\s*=\\s*[\"'](?<href>[^\"']+\\.(?:wad|zip))[\"']",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);

    public string Id =>
        "quaddicted";

    public string DisplayName =>
        "Quaddicted";

    public string Summary =>
        "Quake · WAD2 archive + ZIP packs";

    public string Description =>
        "Long-running Quake texture WAD archive. Companion supports direct WADs and ZIP packages, preserving archive paths so individual WADs can be previewed or imported.";

    public Uri CatalogUri { get; } =
        new(
            "https://www.quaddicted.com/files/wads/",
            UriKind.Absolute);

    public async Task<IReadOnlyList<CompanionOnlineWadEntry>> GetEntriesAsync(
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request =
            new(
                HttpMethod.Get,
                CatalogUri);

        using HttpResponseMessage response =
            await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        string html =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        Dictionary<string, CompanionOnlineWadEntry> entries =
            new(
                StringComparer.OrdinalIgnoreCase);

        foreach (Match match in
                 DownloadHrefRegex.Matches(
                     html))
        {
            string href =
                WebUtility.HtmlDecode(
                    match.Groups["href"].Value)
                .Trim();

            if (!Uri.TryCreate(
                    CatalogUri,
                    href,
                    out Uri? downloadUri) ||
                downloadUri is null ||
                !string.Equals(
                    downloadUri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    downloadUri.Host,
                    CatalogUri.Host,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string extension =
                Path.GetExtension(
                    downloadUri.AbsolutePath);

            if (!string.Equals(
                    extension,
                    ".wad",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    extension,
                    ".zip",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string fileName =
                Uri.UnescapeDataString(
                    downloadUri.Segments[^1]);

            if (string.IsNullOrWhiteSpace(
                    fileName))
            {
                continue;
            }

            CompanionOnlineWadEntry entry =
                new(
                    Id,
                    DisplayName,
                    fileName,
                    GetDisplayName(
                        fileName),
                    "WAD2",
                    "Quake",
                    CatalogUri,
                    downloadUri);

            entries[downloadUri.AbsoluteUri] =
                entry;
        }

        return entries
            .Values
            .OrderBy(
                entry =>
                    entry.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                entry =>
                    entry.FileName,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetDisplayName(
        string fileName)
    {
        string displayName =
            Path.GetFileNameWithoutExtension(
                fileName);

        if (displayName.EndsWith(
                ".wad",
                StringComparison.OrdinalIgnoreCase))
        {
            displayName =
                Path.GetFileNameWithoutExtension(
                    displayName);
        }

        return displayName;
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client =
            new()
            {
                Timeout =
                    TimeSpan.FromSeconds(
                        30)
            };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "TrenchBroom-Companion/1.0");

        return client;
    }
}

public sealed record CompanionOnlineWadDownloadedItem(
    string ArchivePath,
    string FileName,
    string TemporaryPath);

public sealed record CompanionOnlineWadArchiveIssue(
    string ArchivePath,
    string Message);

public sealed record CompanionOnlineWadDownloadResult(
    string SourceFilePath,
    IReadOnlyList<CompanionOnlineWadDownloadedItem> Wads,
    IReadOnlyList<CompanionOnlineWadArchiveIssue> Issues);

public static class CompanionOnlineWadDownloadService
{
    private static readonly HttpClient HttpClient =
        CreateHttpClient();

    public static async Task<CompanionOnlineWadDownloadResult> DownloadPackageAsync(
        CompanionOnlineWadEntry entry,
        string managedDataRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            entry);

        if (string.IsNullOrWhiteSpace(
                managedDataRoot))
        {
            throw new ArgumentException(
                "A Companion managed data root is required.",
                nameof(managedDataRoot));
        }

        string cacheDirectory =
            Path.Combine(
                Path.GetFullPath(
                    managedDataRoot),
                "Cache",
                "OnlineWads",
                Guid.NewGuid().ToString(
                    "N"));

        Directory.CreateDirectory(
            cacheDirectory);

        string provisionalPath =
            Path.Combine(
                cacheDirectory,
                "download.tmp");

        string downloadedPath =
            provisionalPath;

        Uri currentUri =
            entry.DownloadUri;

        Uri? referrer =
            entry.SourcePageUri;

        HashSet<string> visitedUris =
            new(
                StringComparer.OrdinalIgnoreCase);

        try
        {
            for (int hop = 0;
                 hop < 8;
                 hop++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!visitedUris.Add(
                        currentUri.AbsoluteUri))
                {
                    throw new InvalidDataException(
                        $"The community download flow looped back to '{currentUri}'.");
                }

                using HttpRequestMessage request =
                    new(
                        HttpMethod.Get,
                        currentUri);

                AddCommunityBrowserHeaders(
                    request);

                if (referrer is not null &&
                    !string.Equals(
                        referrer.AbsoluteUri,
                        currentUri.AbsoluteUri,
                        StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Referrer =
                        referrer;
                }

                using HttpResponseMessage response =
                    await HttpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);

                response.EnsureSuccessStatusCode();

                Uri responseUri =
                    response.RequestMessage?.RequestUri ??
                    currentUri;

                string? mediaType =
                    response.Content.Headers.ContentType?.MediaType;

                if (!string.IsNullOrWhiteSpace(
                        mediaType) &&
                    mediaType.Contains(
                        "html",
                        StringComparison.OrdinalIgnoreCase))
                {
                    string html =
                        await response.Content.ReadAsStringAsync(
                            cancellationToken);

                    Uri? resolvedDownload =
                        FindDownloadTarget(
                            responseUri,
                            html);

                    if (resolvedDownload is null)
                    {
                        throw new InvalidDataException(
                            $"The community resource page '{entry.DisplayName}' returned HTML but did not expose a downloadable WAD/archive link.");
                    }

                    referrer =
                        responseUri;

                    currentUri =
                        resolvedDownload;

                    continue;
                }

                if (File.Exists(
                        provisionalPath))
                {
                    File.Delete(
                        provisionalPath);
                }

                await using Stream source =
                    await response.Content.ReadAsStreamAsync(
                        cancellationToken);

                await using (FileStream destination =
                    new(
                        provisionalPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        81920,
                        useAsync:
                            true))
                {
                    await source.CopyToAsync(
                        destination,
                        cancellationToken);

                    await destination.FlushAsync(
                        cancellationToken);
                }

                byte[] prefix =
                    new byte[512];

                int prefixLength;

                await using (FileStream signatureStream =
                    new(
                        provisionalPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        4096,
                        useAsync:
                            true))
                {
                    prefixLength =
                        await signatureStream.ReadAsync(
                            prefix.AsMemory(
                                0,
                                prefix.Length),
                            cancellationToken);
                }

                string? detectedExtension =
                    DetectCommunityAssetExtension(
                        prefix,
                        prefixLength);

                if (detectedExtension is null &&
                    LooksLikeHtml(
                        prefix,
                        prefixLength))
                {
                    string html =
                        await File.ReadAllTextAsync(
                            provisionalPath,
                            cancellationToken);

                    File.Delete(
                        provisionalPath);

                    Uri? resolvedDownload =
                        FindDownloadTarget(
                            responseUri,
                            html);

                    if (resolvedDownload is null)
                    {
                        throw new InvalidDataException(
                            $"The community resource '{entry.DisplayName}' returned an HTML landing page but no downloadable WAD/archive link.");
                    }

                    referrer =
                        responseUri;

                    currentUri =
                        resolvedDownload;

                    continue;
                }

                if (detectedExtension is null)
                {
                    throw new InvalidDataException(
                        $"The downloaded community resource '{entry.DisplayName}' is not a recognizable WAD2, WAD3, ZIP, 7z, or RAR asset.");
                }

                string? responseFileName =
                    response.Content.Headers.ContentDisposition?.FileNameStar ??
                    response.Content.Headers.ContentDisposition?.FileName;

                responseFileName =
                    responseFileName?
                        .Trim()
                        .Trim(
                            '"');

                if (!string.IsNullOrWhiteSpace(
                        responseFileName))
                {
                    responseFileName =
                        Path.GetFileName(
                            responseFileName);
                }

                string preferredFileName =
                    responseFileName ??
                    GetFileNameFromUri(
                        responseUri) ??
                    entry.DisplayName;

                string preferredBaseName =
                    Path.GetFileNameWithoutExtension(
                        preferredFileName);

                if (string.IsNullOrWhiteSpace(
                        preferredBaseName) ||
                    string.Equals(
                        preferredBaseName,
                        "download",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        preferredBaseName,
                        "index",
                        StringComparison.OrdinalIgnoreCase))
                {
                    preferredBaseName =
                        entry.DisplayName;
                }

                string finalFileName =
                    SanitizeFileName(
                        preferredBaseName +
                        detectedExtension);

                downloadedPath =
                    Path.Combine(
                        cacheDirectory,
                        finalFileName);

                if (File.Exists(
                        downloadedPath))
                {
                    File.Delete(
                        downloadedPath);
                }

                File.Move(
                    provisionalPath,
                    downloadedPath);

                return await PrepareDownloadedPackageAsync(
                    downloadedPath,
                    cacheDirectory,
                    cancellationToken);
            }

            throw new InvalidDataException(
                $"The community download flow for '{entry.DisplayName}' exceeded the maximum number of HTML/download hops.");
        }
        catch
        {
            DeleteTemporaryDownload(
                downloadedPath);

            if (File.Exists(
                    provisionalPath))
            {
                try
                {
                    File.Delete(
                        provisionalPath);
                }
                catch
                {
                }
            }

            throw;
        }
    }

    public static async Task<string> DownloadTemporaryAsync(
        CompanionOnlineWadEntry entry,
        string managedDataRoot,
        CancellationToken cancellationToken)
    {
        CompanionOnlineWadDownloadResult package =
            await DownloadPackageAsync(
                entry,
                managedDataRoot,
                cancellationToken);

        if (package.Wads.Count ==
            1)
        {
            return package.Wads[0].TemporaryPath;
        }

        DeleteTemporaryDownload(
            package.SourceFilePath);

        throw new InvalidDataException(
            $"The downloaded ZIP '{Path.GetFileName(package.SourceFilePath)}' contains {package.Wads.Count:N0} WAD files. Open it through the community archive browser to choose individual WADs or import them all.");
    }

    private static async Task<CompanionOnlineWadDownloadResult> PrepareDownloadedPackageAsync(
        string downloadedPath,
        string cacheDirectory,
        CancellationToken cancellationToken)
    {
        string extension =
            Path.GetExtension(
                downloadedPath);

        if (string.Equals(
                extension,
                ".wad",
                StringComparison.OrdinalIgnoreCase))
        {
            CompanionOnlineWadDownloadedItem wad =
                new(
                    Path.GetFileName(
                        downloadedPath),
                    Path.GetFileName(
                        downloadedPath),
                    downloadedPath);

            return new CompanionOnlineWadDownloadResult(
                downloadedPath,
                new[]
                {
                    wad
                },
                Array.Empty<CompanionOnlineWadArchiveIssue>());
        }

        if (!IsSupportedCommunityArchiveExtension(
                extension))
        {
            throw new InvalidDataException(
                $"The downloaded community asset '{Path.GetFileName(downloadedPath)}' is not a supported WAD/archive.");
        }

        string extractionDirectory =
            Path.Combine(
                cacheDirectory,
                "Extracted");

        Directory.CreateDirectory(
            extractionDirectory);

        List<CompanionOnlineWadDownloadedItem> extractedWads =
            new();

        List<CompanionOnlineWadArchiveIssue> issues =
            new();

        ReaderOptions readerOptions =
            ReaderOptions.ForFilePath
                .WithExtensionHint(
                    extension.TrimStart(
                        '.'))
                .WithDisableCheckIncomplete(
                    true);

        using IArchive archive =
            ArchiveFactory.OpenArchive(
                downloadedPath,
                readerOptions);

        IArchiveEntry[] wadEntries =
            archive.Entries
                .Where(
                    archiveEntry =>
                        !archiveEntry.IsDirectory &&
                        !string.IsNullOrWhiteSpace(
                            archiveEntry.Key) &&
                        archiveEntry.Key.EndsWith(
                            ".wad",
                            StringComparison.OrdinalIgnoreCase))
                .OrderBy(
                    archiveEntry =>
                        archiveEntry.Key,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (wadEntries.Length ==
            0)
        {
            throw new InvalidDataException(
                $"The downloaded archive '{Path.GetFileName(downloadedPath)}' does not contain a WAD file.");
        }

        for (int index = 0;
             index < wadEntries.Length;
             index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IArchiveEntry wadEntry =
                wadEntries[index];

            string archivePath =
                (wadEntry.Key ??
                 string.Empty)
                    .Replace(
                        '\\',
                        '/')
                    .TrimStart(
                        '/');

            string wadFileName =
                Path.GetFileName(
                    archivePath);

            if (string.IsNullOrWhiteSpace(
                    wadFileName))
            {
                wadFileName =
                    $"archive-{index + 1:N0}.wad";
            }

            string extractedMemberDirectory =
                Path.Combine(
                    extractionDirectory,
                    $"{index + 1:D4}");

            Directory.CreateDirectory(
                extractedMemberDirectory);

            string extractedPath =
                Path.Combine(
                    extractedMemberDirectory,
                    SanitizeFileName(
                        wadFileName));

            try
            {
                using Stream entrySource =
                    wadEntry.OpenEntryStream();

                await using FileStream destination =
                    new(
                        extractedPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        81920,
                        useAsync:
                            true);

                await entrySource.CopyToAsync(
                    destination,
                    cancellationToken);

                await destination.FlushAsync(
                    cancellationToken);

                extractedWads.Add(
                    new CompanionOnlineWadDownloadedItem(
                        archivePath,
                        wadFileName,
                        extractedPath));
            }
            catch (Exception exception)
                when (exception is not
                      OperationCanceledException)
            {
                try
                {
                    if (File.Exists(
                            extractedPath))
                    {
                        File.Delete(
                            extractedPath);
                    }
                }
                catch
                {
                }

                issues.Add(
                    new CompanionOnlineWadArchiveIssue(
                        archivePath,
                        exception.Message));
            }
        }

        if (extractedWads.Count ==
                0 &&
            issues.Count >
                0)
        {
            string firstIssue =
                issues[0].Message;

            throw new InvalidDataException(
                $"The archive contains WAD files, but none could be extracted. First issue: {firstIssue}");
        }

        return new CompanionOnlineWadDownloadResult(
            downloadedPath,
            extractedWads,
            issues);
    }

    private static Uri? FindDownloadTarget(
        Uri pageUri,
        string html)
    {
        Regex anchorRegex =
            new(
                "<a\\b(?<attrs>[^>]*)>(?<text>.*?)</a>",
                RegexOptions.IgnoreCase |
                RegexOptions.Singleline);

        Regex hrefRegex =
            new(
                "href\\s*=\\s*[\"'](?<href>[^\"']+)[\"']",
                RegexOptions.IgnoreCase |
                RegexOptions.Singleline);

        Regex tagRegex =
            new(
                "<[^>]+>",
                RegexOptions.Singleline);

        Uri? bestUri =
            null;

        int bestScore =
            int.MinValue;

        foreach (Match anchor in
                 anchorRegex.Matches(
                     html))
        {
            string attrs =
                anchor.Groups["attrs"].Value;

            Match hrefMatch =
                hrefRegex.Match(
                    attrs);

            if (!hrefMatch.Success)
            {
                continue;
            }

            string href =
                WebUtility.HtmlDecode(
                    hrefMatch.Groups["href"].Value)
                .Trim();

            if (string.IsNullOrWhiteSpace(
                    href) ||
                href.StartsWith(
                    "#",
                    StringComparison.Ordinal) ||
                href.StartsWith(
                    "javascript:",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Uri.TryCreate(
                    pageUri,
                    href,
                    out Uri? candidate) ||
                candidate is null ||
                !string.Equals(
                    candidate.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text =
                Regex.Replace(
                    WebUtility.HtmlDecode(
                        tagRegex.Replace(
                            anchor.Groups["text"].Value,
                            " ")),
                    "\\s+",
                    " ")
                .Trim();

            int score =
                0;

            if (string.Equals(
                    text,
                    "Download",
                    StringComparison.OrdinalIgnoreCase))
            {
                score +=
                    400;
            }
            else if (text.Contains(
                         "Download",
                         StringComparison.OrdinalIgnoreCase))
            {
                score +=
                    250;
            }

            if (href.Contains(
                    "download",
                    StringComparison.OrdinalIgnoreCase))
            {
                score +=
                    220;
            }

            if (attrs.Contains(
                    "button--cta",
                    StringComparison.OrdinalIgnoreCase))
            {
                score +=
                    100;
            }

            if (HasSupportedCommunityAssetExtension(
                    candidate))
            {
                score +=
                    150;
            }

            if (score >
                bestScore)
            {
                bestScore =
                    score;

                bestUri =
                    candidate;
            }
        }

        return bestScore >
            0
            ? bestUri
            : null;
    }

    private static string? DetectCommunityAssetExtension(
        byte[] prefix,
        int length)
    {
        if (length >=
                4 &&
            prefix[0] == (byte)'W' &&
            prefix[1] == (byte)'A' &&
            prefix[2] == (byte)'D' &&
            (prefix[3] == (byte)'2' ||
             prefix[3] == (byte)'3'))
        {
            return ".wad";
        }

        if (length >=
                4 &&
            prefix[0] == (byte)'P' &&
            prefix[1] == (byte)'K')
        {
            return ".zip";
        }

        if (length >=
                6 &&
            prefix[0] == 0x37 &&
            prefix[1] == 0x7A &&
            prefix[2] == 0xBC &&
            prefix[3] == 0xAF &&
            prefix[4] == 0x27 &&
            prefix[5] == 0x1C)
        {
            return ".7z";
        }

        if (length >=
                7 &&
            prefix[0] == 0x52 &&
            prefix[1] == 0x61 &&
            prefix[2] == 0x72 &&
            prefix[3] == 0x21 &&
            prefix[4] == 0x1A &&
            prefix[5] == 0x07)
        {
            return ".rar";
        }

        return null;
    }

    private static bool LooksLikeHtml(
        byte[] prefix,
        int length)
    {
        if (length <=
            0)
        {
            return false;
        }

        string text =
            System.Text.Encoding.UTF8.GetString(
                prefix,
                0,
                length)
            .TrimStart();

        return text.StartsWith(
                   "<!DOCTYPE",
                   StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith(
                   "<html",
                   StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith(
                   "<head",
                   StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith(
                   "<body",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSupportedCommunityAssetExtension(
        Uri uri)
    {
        string extension =
            Path.GetExtension(
                uri.AbsolutePath);

        return string.Equals(
                   extension,
                   ".wad",
                   StringComparison.OrdinalIgnoreCase) ||
               IsSupportedCommunityArchiveExtension(
                   extension);
    }

    private static bool IsSupportedCommunityArchiveExtension(
        string extension)
    {
        return string.Equals(
                   extension,
                   ".zip",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   extension,
                   ".7z",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   extension,
                   ".rar",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetFileNameFromUri(
        Uri uri)
    {
        string fileName =
            Path.GetFileName(
                Uri.UnescapeDataString(
                    uri.AbsolutePath));

        return string.IsNullOrWhiteSpace(
                fileName)
            ? null
            : fileName;
    }

    private static void AddCommunityBrowserHeaders(
        HttpRequestMessage request)
    {
        request.Headers.UserAgent.Clear();

        request.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36");

        request.Headers.Accept.Clear();

        request.Headers.Accept.ParseAdd(
            "text/html,application/xhtml+xml,application/xml;q=0.9,application/octet-stream,*/*;q=0.8");

        request.Headers.AcceptLanguage.Clear();

        request.Headers.AcceptLanguage.ParseAdd(
            "en-US,en;q=0.9");
    }

    private static string SanitizeFileName(
        string fileName)
    {
        HashSet<char> invalid =
            Path.GetInvalidFileNameChars()
                .ToHashSet();

        char[] characters =
            fileName
                .Select(
                    character =>
                        invalid.Contains(
                            character)
                            ? '_'
                            : character)
                .ToArray();

        string sanitized =
            new(
                characters);

        return string.IsNullOrWhiteSpace(
                sanitized)
            ? "archive.wad"
            : sanitized;
    }

    public static void DeleteTemporaryDownload(
        string? temporaryPath)
    {
        if (string.IsNullOrWhiteSpace(
                temporaryPath))
        {
            return;
        }

        try
        {
            string fullPath =
                Path.GetFullPath(
                    temporaryPath);

            string? directory =
                Directory.Exists(
                    fullPath)
                    ? fullPath
                    : Path.GetDirectoryName(
                        fullPath);

            if (string.IsNullOrWhiteSpace(
                    directory))
            {
                return;
            }

            DirectoryInfo? current =
                new(
                    directory);

            while (current is not
                   null)
            {
                if (current.Parent is not
                        null &&
                    string.Equals(
                        current.Parent.Name,
                        "OnlineWads",
                        StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(
                        current.FullName,
                        recursive:
                            true);

                    return;
                }

                current =
                    current.Parent;
            }

            if (Directory.Exists(
                    directory))
            {
                Directory.Delete(
                    directory,
                    recursive:
                        true);
            }
        }
        catch
        {
        }
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client =
            new()
            {
                Timeout =
                    TimeSpan.FromMinutes(
                        2)
            };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "TrenchBroom-Companion/1.0");

        return client;
    }
}
