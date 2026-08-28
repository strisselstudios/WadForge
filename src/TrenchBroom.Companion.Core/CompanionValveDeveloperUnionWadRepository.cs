using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TrenchBroom.Companion.Core;

public sealed class CompanionValveDeveloperUnionWadRepository :
    ICompanionOnlineWadRepository
{
    private static readonly IReadOnlyList<CompanionOnlineWadEntry> Entries =
        new[]
        {
            new CompanionOnlineWadEntry(
                "vdu",
                "Valve Developer Union",
                "quake_old_wads.zip",
                "Original Quake WADs",
                "WAD2",
                "Quake",
                new Uri(
                    "https://valvedev.info/tools/quake-map-sources-and-original-wads/",
                    UriKind.Absolute),
                new Uri(
                    "https://valvedev.info/tools/quake-map-sources-and-original-wads/quake_old_wads.zip",
                    UriKind.Absolute)),
            new CompanionOnlineWadEntry(
                "vdu",
                "Valve Developer Union",
                "quake_overhaul_wads.zip",
                "Quake WAD Overhaul Project",
                "WAD2",
                "Quake",
                new Uri(
                    "https://valvedev.info/tools/quake-wad-overhaul-project/",
                    UriKind.Absolute),
                new Uri(
                    "https://valvedev.info/tools/quake-wad-overhaul-project/quake_overhaul_wads.zip",
                    UriKind.Absolute))
        };

    public string Id =>
        "vdu";

    public string DisplayName =>
        "Valve Developer Union";

    public string Summary =>
        "Quake · WAD2 curated collections";

    public string Description =>
        "Preserved Quake WAD collections from the Valve Developer Union archive, including the original map-source WAD bundle and the curated Quake WAD Overhaul Project. Companion validates downloaded WAD contents before preview or import.";

    public Uri CatalogUri { get; } =
        new(
            "https://valvedev.info/tools/",
            UriKind.Absolute);

    public Task<IReadOnlyList<CompanionOnlineWadEntry>> GetEntriesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            Entries);
    }
}
