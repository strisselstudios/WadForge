using System;
using System.Collections.Generic;
using System.IO;
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
            new CompanionQuaketasticWadRepository()
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

public static class CompanionOnlineWadDownloadService
{
    private static readonly HttpClient HttpClient =
        CreateHttpClient();

    public static async Task<string> DownloadTemporaryAsync(
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

        string safeFileName =
            Path.GetFileName(
                entry.FileName);

        if (string.IsNullOrWhiteSpace(
                safeFileName))
        {
            safeFileName =
                "download.wad";
        }

        string temporaryPath =
            Path.Combine(
                cacheDirectory,
                safeFileName);

        try
        {
            using HttpRequestMessage request =
                new(
                    HttpMethod.Get,
                    entry.DownloadUri);

            using HttpResponseMessage response =
                await HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            response.EnsureSuccessStatusCode();

            await using Stream source =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);

            await using FileStream destination =
                new(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync:
                        true);

            await source.CopyToAsync(
                destination,
                cancellationToken);

            return temporaryPath;
        }
        catch
        {
            DeleteTemporaryDownload(
                temporaryPath);

            throw;
        }
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
            string? directory =
                Path.GetDirectoryName(
                    temporaryPath);

            if (!string.IsNullOrWhiteSpace(
                    directory) &&
                Directory.Exists(
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