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

public sealed class CompanionSlipseerWadRepository :
    ICompanionOnlineWadRepository
{
    private const int MaximumCatalogPages =
        20;

    private static readonly HttpClient HttpClient =
        CreateHttpClient();

    private static readonly Regex ResourceTitleLinkRegex =
        new(
            "<div\\b[^>]*class\\s*=\\s*[\"'][^\"']*structItem-title[^\"']*[\"'][^>]*>.*?<a\\b[^>]*href\\s*=\\s*[\"'](?<href>[^\"']+)[\"'][^>]*>(?<text>.*?)</a>",
            RegexOptions.IgnoreCase |
            RegexOptions.Singleline |
            RegexOptions.Compiled);

    private static readonly Regex ResourceIdRegex =
        new(
            "(?:resources/|resources%2F)(?:[^/?#&\"']*?\\.)?(?<id>\\d+)(?:/|%2F|$)",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);

    private static readonly Regex AnchorRegex =
        new(
            "<a\\b(?<attrs>[^>]*)>(?<text>.*?)</a>",
            RegexOptions.IgnoreCase |
            RegexOptions.Singleline |
            RegexOptions.Compiled);

    private static readonly Regex HrefRegex =
        new(
            "href\\s*=\\s*[\"'](?<href>[^\"']+)[\"']",
            RegexOptions.IgnoreCase |
            RegexOptions.Singleline |
            RegexOptions.Compiled);

    private static readonly Regex HtmlTagRegex =
        new(
            "<[^>]+>",
            RegexOptions.Singleline |
            RegexOptions.Compiled);

    public string Id =>
        "slipseer";

    public string DisplayName =>
        "Slipseer Asset Hub";

    public string Summary =>
        "Quake · WAD2 community texture packs";

    public string Description =>
        "Slipseer's Texture Wads category contains community-made Quake texture resources. Companion enumerates every category page, accepts both canonical and numeric-only Slipseer resource links, requires the discovered total to match Slipseer's own category count, follows each resource's real Download link, validates WAD contents, and imports only compatible WADs into the global library.";

    public Uri CatalogUri { get; } =
        new(
            "https://www.slipseer.com/resources/categories/texture-wads.6/",
            UriKind.Absolute);

    public async Task<IReadOnlyList<CompanionOnlineWadEntry>> GetEntriesAsync(
        CancellationToken cancellationToken)
    {
        Dictionary<string, CompanionOnlineWadEntry> entries =
            new(
                StringComparer.OrdinalIgnoreCase);

        HashSet<string> visitedPages =
            new(
                StringComparer.OrdinalIgnoreCase);

        int? declaredResourceCount =
            null;

        Uri? pageUri =
            CatalogUri;

        for (int pageNumber = 1;
             pageUri is not null &&
             pageNumber <= MaximumCatalogPages;
             pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!visitedPages.Add(
                    pageUri.AbsoluteUri))
            {
                throw new InvalidDataException(
                    "Slipseer Texture Wads pagination looped back to an already-visited page.");
            }

            string html =
                await DownloadHtmlAsync(
                    pageUri,
                    cancellationToken);

            if (!LooksLikeTextureWadsCategory(
                    html))
            {
                throw new InvalidDataException(
                    $"Slipseer did not return the Texture Wads category for '{pageUri}'. Companion will not silently show a partial catalog.");
            }

            declaredResourceCount ??=
                ReadDeclaredCategoryCount(
                    html);

            MatchCollection titleMatches =
                ResourceTitleLinkRegex.Matches(
                    html);

            if (titleMatches.Count ==
                0)
            {
                throw new InvalidDataException(
                    $"Slipseer Texture Wads page {pageNumber:N0} contained no resource title rows. Companion will not silently show a partial catalog.");
            }

            int parsedOnPage =
                0;

            foreach (Match titleMatch in
                     titleMatches)
            {
                string href =
                    WebUtility.HtmlDecode(
                        titleMatch.Groups["href"].Value)
                    .Trim();

                Match idMatch =
                    ResourceIdRegex.Match(
                        href);

                if (!idMatch.Success)
                {
                    continue;
                }

                string resourceId =
                    idMatch.Groups["id"].Value;

                if (string.IsNullOrWhiteSpace(
                        resourceId))
                {
                    continue;
                }

                if (!Uri.TryCreate(
                        pageUri,
                        href,
                        out Uri? sourcePageUri) ||
                    sourcePageUri is null ||
                    !string.Equals(
                        sourcePageUri.Scheme,
                        Uri.UriSchemeHttps,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        sourcePageUri.Host,
                        CatalogUri.Host,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string displayName =
                    CleanLinkText(
                        titleMatch.Groups["text"].Value);

                if (string.IsNullOrWhiteSpace(
                        displayName) ||
                    string.Equals(
                        displayName,
                        "Resource icon",
                        StringComparison.OrdinalIgnoreCase))
                {
                    displayName =
                        $"Slipseer Texture WAD {resourceId}";
                }

                string fileStem =
                    BuildSafeFileStem(
                        displayName,
                        resourceId);

                CompanionOnlineWadEntry entry =
                    new(
                        Id,
                        DisplayName,
                        $"{fileStem}.download",
                        displayName,
                        "WAD2",
                        "Quake",
                        sourcePageUri,
                        sourcePageUri);

                entries[resourceId] =
                    entry;

                parsedOnPage++;
            }

            if (parsedOnPage ==
                0)
            {
                throw new InvalidDataException(
                    $"Slipseer Texture Wads page {pageNumber:N0} exposed resource rows, but none had a recognizable resource ID.");
            }

            pageUri =
                ResolveNextPageUri(
                    pageUri,
                    html);
        }

        if (pageUri is not null)
        {
            throw new InvalidDataException(
                $"Slipseer Texture Wads exceeded the safety limit of {MaximumCatalogPages:N0} pages. Companion will not silently truncate the catalog.");
        }

        if (declaredResourceCount is null)
        {
            throw new InvalidDataException(
                "Companion could not read Slipseer's declared Texture Wads resource count, so it cannot prove the catalog is complete.");
        }

        if (entries.Count !=
            declaredResourceCount.Value)
        {
            throw new InvalidDataException(
                $"Slipseer declares {declaredResourceCount.Value:N0} Texture Wads, but Companion discovered {entries.Count:N0}. The source will not be shown as a partial catalog.");
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

    private static string BuildSafeFileStem(
        string displayName,
        string resourceId)
    {
        char[] invalidChars =
            Path.GetInvalidFileNameChars();

        string cleaned =
            new(
                displayName
                    .Where(
                        character =>
                            !invalidChars.Contains(
                                character))
                    .ToArray());

        cleaned =
            Regex.Replace(
                cleaned,
                "\\s+",
                "-")
            .Trim(
                '-',
                '.');

        if (string.IsNullOrWhiteSpace(
                cleaned))
        {
            cleaned =
                $"slipseer-{resourceId}";
        }

        return cleaned;
    }

    private static int? ReadDeclaredCategoryCount(
        string html)
    {
        int searchIndex =
            0;

        while (searchIndex <
               html.Length)
        {
            int markerIndex =
                html.IndexOf(
                    "texture-wads.6",
                    searchIndex,
                    StringComparison.OrdinalIgnoreCase);

            if (markerIndex <
                0)
            {
                break;
            }

            int contextStart =
                Math.Max(
                    0,
                    markerIndex -
                    300);

            int contextLength =
                Math.Min(
                    900,
                    html.Length -
                    contextStart);

            string context =
                CleanLinkText(
                    html.Substring(
                        contextStart,
                        contextLength));

            Match countMatch =
                Regex.Match(
                    context,
                    "Texture Wads\\s+(?<count>\\d+)",
                    RegexOptions.IgnoreCase);

            if (countMatch.Success &&
                int.TryParse(
                    countMatch.Groups["count"].Value,
                    out int count) &&
                count >
                    0)
            {
                return count;
            }

            searchIndex =
                markerIndex +
                "texture-wads.6".Length;
        }

        return null;
    }

    private static Uri? ResolveNextPageUri(
        Uri currentPageUri,
        string html)
    {
        foreach (Match anchor in
                 AnchorRegex.Matches(
                     html))
        {
            string attrs =
                anchor.Groups["attrs"].Value;

            string text =
                CleanLinkText(
                    anchor.Groups["text"].Value);

            bool isNext =
                attrs.Contains(
                    "pageNav-jump--next",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    text,
                    "Next",
                    StringComparison.OrdinalIgnoreCase);

            if (!isNext)
            {
                continue;
            }

            Match hrefMatch =
                HrefRegex.Match(
                    attrs);

            if (!hrefMatch.Success)
            {
                continue;
            }

            string href =
                WebUtility.HtmlDecode(
                    hrefMatch.Groups["href"].Value)
                .Trim();

            if (!Uri.TryCreate(
                    currentPageUri,
                    href,
                    out Uri? nextPageUri) ||
                nextPageUri is null ||
                !string.Equals(
                    nextPageUri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    nextPageUri.Host,
                    currentPageUri.Host,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return nextPageUri;
        }

        return null;
    }

    private static bool LooksLikeTextureWadsCategory(
        string html)
    {
        return html.Contains(
                   "Texture Wads",
                   StringComparison.OrdinalIgnoreCase) &&
               html.Contains(
                   ".wad files containing Quake textures",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> DownloadHtmlAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request =
            new(
                HttpMethod.Get,
                uri);

        request.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36");

        request.Headers.Accept.ParseAdd(
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

        request.Headers.AcceptLanguage.ParseAdd(
            "en-US,en;q=0.9");

        using HttpResponseMessage response =
            await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(
            cancellationToken);
    }

    private static string CleanLinkText(
        string html)
    {
        string withoutTags =
            HtmlTagRegex.Replace(
                html,
                " ");

        string decoded =
            WebUtility.HtmlDecode(
                withoutTags);

        return Regex.Replace(
                decoded,
                "\\s+",
                " ")
            .Trim();
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClientHandler handler =
            new()
            {
                AllowAutoRedirect =
                    true,
                UseCookies =
                    true,
                CookieContainer =
                    new CookieContainer(),
                AutomaticDecompression =
                    DecompressionMethods.All
            };

        return new HttpClient(
            handler)
        {
            Timeout =
                TimeSpan.FromSeconds(
                    45)
        };
    }
}
