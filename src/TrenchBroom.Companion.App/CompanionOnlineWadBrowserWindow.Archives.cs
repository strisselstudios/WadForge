using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TrenchBroom.Companion.Core;

namespace TrenchBroom.Companion.App;

public partial class CompanionOnlineWadBrowserWindow
{
    private bool _archiveAwareHandlersInstalled;
    private bool _communityCompatibilityFilterInstalled;
    private bool _communityCompatibilityFilterPending;
    private bool _applyingCommunityCompatibilityFilter;

    private readonly HashSet<string>
        _hiddenCommunityPackageUris =
            new(
                StringComparer.OrdinalIgnoreCase);

    private CompanionCommunityWadCompatibilityCatalog?
        _communityCompatibilityCatalog;

    protected override void OnContentRendered(
        EventArgs e)
    {
        base.OnContentRendered(
            e);

        if (_archiveAwareHandlersInstalled)
        {
            return;
        }

        _archiveAwareHandlersInstalled =
            true;

        EnsureCommunityDisclaimer();

        _communityCompatibilityCatalog =
            CompanionCommunityWadCompatibilityCatalog.Load(
                _managedDataRoot);

        if (!_communityCompatibilityFilterInstalled)
        {
            _communityCompatibilityFilterInstalled =
                true;

            _visibleEntries.CollectionChanged +=
                VisibleCommunityEntries_CollectionChanged;
        }

        ApplyKnownCommunityCompatibilityFilter();

        PreviewOnlineWadButton.Click -=
            PreviewOnlineWadButton_Click;
        PreviewOnlineWadButton.Click +=
            PreviewOnlineWadArchiveAwareButton_Click;

        ImportOnlineWadButton.Click -=
            ImportOnlineWadButton_Click;
        ImportOnlineWadButton.Click +=
            ImportOnlineWadArchiveAwareButton_Click;

        OnlineWadGrid.MouseDoubleClick -=
            OnlineWadGrid_MouseDoubleClick;
        OnlineWadGrid.MouseDoubleClick +=
            OnlineWadGrid_ArchiveAwareMouseDoubleClick;
    }

    private async void PreviewOnlineWadArchiveAwareButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await HandleSelectedCommunityPackageAsync(
            importSingleWad:
                false);
    }

    private async void ImportOnlineWadArchiveAwareButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await HandleSelectedCommunityPackageAsync(
            importSingleWad:
                true);
    }

    private async void OnlineWadGrid_ArchiveAwareMouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (OnlineWadGrid.SelectedItem is not
            CompanionOnlineWadEntry)
        {
            return;
        }

        await HandleSelectedCommunityPackageAsync(
            importSingleWad:
                false);
    }

    private async Task HandleSelectedCommunityPackageAsync(
        bool importSingleWad)
    {
        if (OnlineWadGrid.SelectedItem is not
            CompanionOnlineWadEntry entry)
        {
            return;
        }

        CompanionOnlineWadDownloadResult? package =
            null;

        SetBusy(
            true);

        OnlineStatusText.Text =
            $"Downloading {entry.FileName}...";

        try
        {
            package =
                await CompanionOnlineWadDownloadService.DownloadPackageAsync(
                    entry,
                    _managedDataRoot,
                    CancellationToken.None);

            IReadOnlyList<CompanionOnlineWadDownloadedItem>
                applicableWads =
                    FilterApplicableCommunityWads(
                        package.Wads,
                        out int hiddenWadCount);

            int hiddenItemCount =
                hiddenWadCount +
                package.Issues.Count;

            foreach (CompanionOnlineWadArchiveIssue issue in
                     package.Issues)
            {
                Debug.WriteLine(
                    $"[Community WAD] Hidden archive item '{issue.ArchivePath}': {issue.Message}");
            }

            if (applicableWads.Count ==
                0)
            {
                HideCommunityPackage(
                    entry,
                    CompanionCommunityWadCompatibilityState.Incompatible,
                    "No compatible WAD2/WAD3 brush-texture assets were found.");

                OnlineStatusText.Text =
                    $"Hidden {entry.FileName}: no compatible WAD2/WAD3 brush-texture assets were found.";

                return;
            }

            RememberCompatibleCommunityPackage(
                entry,
                applicableWads.Count,
                hiddenItemCount);

            if (applicableWads.Count ==
                    1 &&
                hiddenItemCount ==
                    0)
            {
                CompanionOnlineWadDownloadedItem wad =
                    applicableWads[0];

                if (importSingleWad)
                {
                    await ImportDownloadedWadsAsync(
                        entry,
                        new[]
                        {
                            wad
                        },
                        this);
                }
                else
                {
                    await PreviewDownloadedWadAsync(
                        entry,
                        wad,
                        this);
                }

                return;
            }

            CompanionOnlineWadArchiveDialog dialog =
                new(
                    entry,
                    applicableWads,
                    hiddenItemCount,
                    (wad, owner) =>
                        PreviewDownloadedWadAsync(
                            entry,
                            wad,
                            owner),
                    (wads, owner) =>
                        ImportDownloadedWadsAsync(
                            entry,
                            wads,
                            owner))
                {
                    Owner =
                        this
                };

            string hiddenSuffix =
                hiddenItemCount ==
                    0
                    ? string.Empty
                    : $" {hiddenItemCount:N0} incompatible item{(hiddenItemCount == 1 ? string.Empty : "s")} hidden.";

            OnlineStatusText.Text =
                $"{entry.FileName} contains {applicableWads.Count:N0} compatible WAD{(applicableWads.Count == 1 ? string.Empty : "s")}.{hiddenSuffix}";

            dialog.ShowDialog();

            if (dialog.ImportedCount >
                0)
            {
                OnlineStatusText.Text =
                    dialog.ImportedCount ==
                    1
                        ? "1 WAD from this community archive was added to or reused from the global library."
                        : $"{dialog.ImportedCount:N0} WADs from this community archive were added to or reused from the global library.";
            }
        }
        catch (Exception exception)
        {
            if (ShouldHideCommunityPackageAfterFailure(
                    exception))
            {
                Debug.WriteLine(
                    $"[Community WAD] Hidden '{entry.FileName}': {exception.Message}");

                CompanionCommunityWadCompatibilityState failureState =
                    GetCompatibilityStateAfterFailure(
                        exception);

                HideCommunityPackage(
                    entry,
                    failureState,
                    exception.Message);

                OnlineStatusText.Text =
                    $"Hidden {entry.FileName}: the external package is unavailable or incompatible.";

                return;
            }

            MessageBox.Show(
                this,
                exception.Message,
                importSingleWad
                    ? "Online WAD Import"
                    : "Online WAD Preview",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            OnlineStatusText.Text =
                importSingleWad
                    ? "Import failed."
                    : "Preview failed.";
        }
        finally
        {
            if (package is not
                null)
            {
                CompanionOnlineWadDownloadService.DeleteTemporaryDownload(
                    package.SourceFilePath);
            }

            SetBusy(
                false);
        }
    }

    private void EnsureCommunityDisclaimer()
    {
        if (Content is not
                Grid root)
        {
            return;
        }

        Grid? headerGrid =
            root.Children
                .OfType<Grid>()
                .FirstOrDefault(
                    child =>
                        Grid.GetRow(
                            child) ==
                        0);

        StackPanel? headerStack =
            headerGrid?
                .Children
                .OfType<StackPanel>()
                .FirstOrDefault();

        if (headerStack is
            null)
        {
            return;
        }

        bool alreadyAdded =
            headerStack.Children
                .OfType<TextBlock>()
                .Any(
                    text =>
                        string.Equals(
                            text.Tag as string,
                            "CommunityRepositoryDisclaimer",
                            StringComparison.Ordinal));

        if (alreadyAdded)
        {
            return;
        }

        Brush mutedBrush =
            TryFindResource(
                "MutedTextBrush") as Brush ??
            Brushes.Gray;

        TextBlock disclaimer =
            new()
            {
                Tag =
                    "CommunityRepositoryDisclaimer",
                Margin =
                    new Thickness(
                        0,
                        7,
                        0,
                        0),
                MaxWidth =
                    800,
                Foreground =
                    mutedBrush,
                FontSize =
                    11,
                TextWrapping =
                    TextWrapping.Wrap,
                Text =
                    "Community WADs come from external repositories. Links, availability, archive contents, and compatibility may change; Companion validates downloads before preview or import."
            };

        headerStack.Children.Add(
            disclaimer);
    }

    private void VisibleCommunityEntries_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        if (_applyingCommunityCompatibilityFilter ||
            _communityCompatibilityFilterPending)
        {
            return;
        }

        _communityCompatibilityFilterPending =
            true;

        _ =
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(
                    ApplyKnownCommunityCompatibilityFilter));
    }

    private void ApplyKnownCommunityCompatibilityFilter()
    {
        _communityCompatibilityFilterPending =
            false;

        if (_allEntries.Count ==
            0)
        {
            return;
        }

        _applyingCommunityCompatibilityFilter =
            true;

        try
        {
            CompanionOnlineWadEntry[] remaining =
                _allEntries
                    .Where(
                        entry =>
                            !IsHiddenCommunityPackage(
                                entry))
                    .ToArray();

            if (remaining.Length !=
                _allEntries.Count)
            {
                _allEntries =
                    remaining;

                ApplySearchFilter();
            }
        }
        finally
        {
            _applyingCommunityCompatibilityFilter =
                false;
        }
    }

    private bool IsHiddenCommunityPackage(
        CompanionOnlineWadEntry entry)
    {
        if (_hiddenCommunityPackageUris.Contains(
                entry.DownloadUri.AbsoluteUri))
        {
            return true;
        }

        return _communityCompatibilityCatalog?
            .ShouldHide(
                entry) ==
            true;
    }

    private void HideCommunityPackage(
        CompanionOnlineWadEntry entry,
        CompanionCommunityWadCompatibilityState state,
        string reason)
    {
        _hiddenCommunityPackageUris.Add(
            entry.DownloadUri.AbsoluteUri);

        _communityCompatibilityCatalog ??=
            CompanionCommunityWadCompatibilityCatalog.Load(
                _managedDataRoot);

        _communityCompatibilityCatalog.Record(
            entry,
            state,
            reason,
            compatibleWadCount:
                0,
            hiddenItemCount:
                0);

        ApplyKnownCommunityCompatibilityFilter();
    }

    private void RememberCompatibleCommunityPackage(
        CompanionOnlineWadEntry entry,
        int compatibleWadCount,
        int hiddenItemCount)
    {
        _communityCompatibilityCatalog ??=
            CompanionCommunityWadCompatibilityCatalog.Load(
                _managedDataRoot);

        _communityCompatibilityCatalog.Record(
            entry,
            CompanionCommunityWadCompatibilityState.Compatible,
            hiddenItemCount ==
                0
                ? "Validated by Companion."
                : $"Validated by Companion; {hiddenItemCount:N0} incompatible archive item{(hiddenItemCount == 1 ? string.Empty : "s")} hidden.",
            compatibleWadCount,
            hiddenItemCount);
    }

    private static bool ShouldHideCommunityPackageAfterFailure(
        Exception exception)
    {
        if (exception is
                InvalidDataException or
                NotSupportedException)
        {
            return true;
        }

        return exception is
                HttpRequestException httpException &&
            httpException.StatusCode is
                HttpStatusCode.NotFound or
                HttpStatusCode.Gone;
    }

    private static CompanionCommunityWadCompatibilityState
        GetCompatibilityStateAfterFailure(
            Exception exception)
    {
        if (exception is
                HttpRequestException httpException &&
            httpException.StatusCode is
                HttpStatusCode.NotFound or
                HttpStatusCode.Gone)
        {
            return CompanionCommunityWadCompatibilityState.Unavailable;
        }

        return CompanionCommunityWadCompatibilityState.Incompatible;
    }

    private static IReadOnlyList<CompanionOnlineWadDownloadedItem>
        FilterApplicableCommunityWads(
            IReadOnlyList<CompanionOnlineWadDownloadedItem> wads,
            out int hiddenWadCount)
    {
        List<CompanionOnlineWadDownloadedItem> applicable =
            new();

        hiddenWadCount =
            0;

        foreach (CompanionOnlineWadDownloadedItem wad in
                 wads)
        {
            if (TryValidateApplicableCommunityWad(
                    wad,
                    out string reason))
            {
                applicable.Add(
                    wad);

                continue;
            }

            hiddenWadCount++;

            Debug.WriteLine(
                $"[Community WAD] Hidden WAD '{wad.ArchivePath}': {reason}");
        }

        return applicable;
    }

    private static bool TryValidateApplicableCommunityWad(
        CompanionOnlineWadDownloadedItem wad,
        out string reason)
    {
        WadRegistrationResult inspection =
            WadRegistrationService.Inspect(
                wad.TemporaryPath);

        if (!inspection.WadIsValid)
        {
            reason =
                inspection.Validation;

            return false;
        }

        if (!string.Equals(
                inspection.WadFormat,
                "WAD2",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                inspection.WadFormat,
                "WAD3",
                StringComparison.OrdinalIgnoreCase))
        {
            reason =
                $"Unsupported WAD format '{inspection.WadFormat}'.";

            return false;
        }

        try
        {
            CompanionRobustWadTextureCatalog catalog =
                CompanionRobustWadTextureCatalogService.ReadCatalog(
                    wad.TemporaryPath);

            if (catalog.Textures.Count ==
                0)
            {
                reason =
                    "No usable brush miptextures were found.";

                return false;
            }

            reason =
                string.Empty;

            return true;
        }
        catch (Exception exception)
        {
            reason =
                exception.Message;

            return false;
        }
    }

    private async Task PreviewDownloadedWadAsync(
        CompanionOnlineWadEntry sourceEntry,
        CompanionOnlineWadDownloadedItem downloadedWad,
        Window owner)
    {
        await Task.Yield();

        WadRegistrationResult inspection =
            WadRegistrationService.Inspect(
                downloadedWad.TemporaryPath);

        if (!inspection.WadIsValid)
        {
            throw new InvalidDataException(
                $"'{downloadedWad.ArchivePath}' is not a valid WAD2/WAD3 archive. {inspection.Validation}");
        }

        CompanionPaletteResolution paletteResolution =
            PreparePaletteResolution(
                sourceEntry,
                inspection);

        CompanionWadBrowserWindow dialog =
            new(
                inspection,
                _managedDataRoot,
                paletteResolution)
            {
                Owner =
                    owner
            };

        OnlineStatusText.Text =
            $"Previewing {downloadedWad.ArchivePath}. Companion detected {inspection.WadFormat}.";

        dialog.ShowDialog();
    }

    private async Task<int> ImportDownloadedWadsAsync(
        CompanionOnlineWadEntry sourceEntry,
        IReadOnlyList<CompanionOnlineWadDownloadedItem> downloadedWads,
        Window owner)
    {
        await Task.Yield();

        int completed =
            0;

        List<string> failures =
            new();

        foreach (CompanionOnlineWadDownloadedItem downloadedWad in
                 downloadedWads)
        {
            try
            {
                WadRegistrationResult inspection =
                    WadRegistrationService.Inspect(
                        downloadedWad.TemporaryPath);

                if (!inspection.WadIsValid)
                {
                    throw new InvalidDataException(
                        inspection.Validation);
                }

                CompanionWadLibraryImportResult result =
                    _wadLibraryService.Import(
                        _managedDataRoot,
                        downloadedWad.TemporaryPath);

                RememberSourcePalette(
                    sourceEntry,
                    result);

                ImportedCount++;
                completed++;
            }
            catch (Exception exception)
            {
                failures.Add(
                    $"{downloadedWad.ArchivePath}: {exception.Message}");
            }
        }

        if (failures.Count >
            0)
        {
            string details =
                string.Join(
                    Environment.NewLine,
                    failures.Take(
                        8));

            if (failures.Count >
                8)
            {
                details +=
                    Environment.NewLine +
                    $"...and {failures.Count - 8:N0} more failure(s).";
            }

            MessageBox.Show(
                owner,
                $"Some WADs could not be imported:{Environment.NewLine}{Environment.NewLine}{details}",
                "Community WAD Archive",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        if (completed >
            0)
        {
            OnlineStatusText.Text =
                completed ==
                1
                    ? "1 WAD was added to or reused from the global Companion library."
                    : $"{completed:N0} WADs were added to or reused from the global Companion library.";
        }

        return completed;
    }
}

internal sealed class CompanionOnlineWadArchiveDialog :
    Window
{
    private static readonly SolidColorBrush WindowBackground =
        CreateBrush(
            0x0E,
            0x12,
            0x18);

    private static readonly SolidColorBrush PanelBackground =
        CreateBrush(
            0x17,
            0x1E,
            0x27);

    private static readonly SolidColorBrush PanelBorder =
        CreateBrush(
            0x2B,
            0x37,
            0x45);

    private static readonly SolidColorBrush PrimaryText =
        CreateBrush(
            0xFF,
            0xFF,
            0xFF);

    private static readonly SolidColorBrush SecondaryText =
        CreateBrush(
            0xAE,
            0xB9,
            0xC7);

    private static readonly SolidColorBrush MutedText =
        CreateBrush(
            0x74,
            0x81,
            0x93);

    private static readonly SolidColorBrush Accent =
        CreateBrush(
            0x5E,
            0xA2,
            0xFF);

    private static readonly SolidColorBrush WarningText =
        CreateBrush(
            0xFF,
            0xC8,
            0x66);

    private static readonly SolidColorBrush ButtonBackground =
        CreateBrush(
            0x26,
            0x31,
            0x3E);

    private static readonly SolidColorBrush PrimaryButtonBackground =
        CreateBrush(
            0x27,
            0x68,
            0xAE);

    private readonly CompanionOnlineWadEntry
        _sourceEntry;

    private readonly IReadOnlyList<CompanionOnlineWadDownloadedItem>
        _wads;

    private readonly int
        _hiddenItemCount;

    private readonly Func<CompanionOnlineWadDownloadedItem, Window, Task>
        _previewAsync;

    private readonly Func<IReadOnlyList<CompanionOnlineWadDownloadedItem>, Window, Task<int>>
        _importAsync;

    private readonly TreeView _tree =
        new();

    private readonly TextBlock _statusText =
        new();

    private readonly Button _previewButton;
    private readonly Button _addSelectedButton;
    private readonly Button _importAllButton;
    private readonly Button _closeButton;

    private bool _busy;

    public CompanionOnlineWadArchiveDialog(
        CompanionOnlineWadEntry sourceEntry,
        IReadOnlyList<CompanionOnlineWadDownloadedItem> wads,
        int hiddenItemCount,
        Func<CompanionOnlineWadDownloadedItem, Window, Task> previewAsync,
        Func<IReadOnlyList<CompanionOnlineWadDownloadedItem>, Window, Task<int>> importAsync)
    {
        _sourceEntry =
            sourceEntry ??
            throw new ArgumentNullException(
                nameof(sourceEntry));

        _wads =
            wads ??
            throw new ArgumentNullException(
                nameof(wads));

        _hiddenItemCount =
            Math.Max(
                0,
                hiddenItemCount);

        _previewAsync =
            previewAsync ??
            throw new ArgumentNullException(
                nameof(previewAsync));

        _importAsync =
            importAsync ??
            throw new ArgumentNullException(
                nameof(importAsync));

        Title =
            "Community WAD Archive";

        Width =
            920;

        Height =
            680;

        MinWidth =
            920;

        MinHeight =
            680;

        MaxWidth =
            920;

        MaxHeight =
            680;

        ResizeMode =
            ResizeMode.NoResize;

        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;

        ShowInTaskbar =
            false;

        Background =
            WindowBackground;

        Foreground =
            PrimaryText;

        Grid root =
            new()
            {
                Margin =
                    new Thickness(
                        26)
            };

        for (int index = 0;
             index < 6;
             index++)
        {
            root.RowDefinitions.Add(
                new RowDefinition
                {
                    Height =
                        index ==
                            2
                            ? new GridLength(
                                1,
                                GridUnitType.Star)
                            : GridLength.Auto
                });
        }

        TextBlock title =
            new()
            {
                Text =
                    _sourceEntry.FileName,
                Foreground =
                    PrimaryText,
                FontSize =
                    24,
                FontWeight =
                    FontWeights.Bold
            };

        root.Children.Add(
            title);

        TextBlock description =
            new()
            {
                Margin =
                    new Thickness(
                        0,
                        8,
                        0,
                        16),
                Text =
                    $"This archive contains {_wads.Count:N0} compatible WAD{(_wads.Count == 1 ? string.Empty : "s")}. Expand folders when present, then select a WAD to preview or add individually, or import all compatible WADs into the global Companion library." +
                    (_hiddenItemCount == 0
                        ? string.Empty
                        : $" {_hiddenItemCount:N0} incompatible item{(_hiddenItemCount == 1 ? string.Empty : "s")} hidden."),
                Foreground =
                    SecondaryText,
                FontSize =
                    12,
                TextWrapping =
                    TextWrapping.Wrap
            };

        Grid.SetRow(
            description,
            1);

        root.Children.Add(
            description);

        Border treeBorder =
            new()
            {
                Padding =
                    new Thickness(
                        10),
                Background =
                    PanelBackground,
                BorderBrush =
                    PanelBorder,
                BorderThickness =
                    new Thickness(
                        1),
                CornerRadius =
                    new CornerRadius(
                        8)
            };

        _tree.Background =
            PanelBackground;

        _tree.Foreground =
            PrimaryText;

        _tree.BorderThickness =
            new Thickness(
                0);

        _tree.Padding =
            new Thickness(
                2);

        _tree.SelectedItemChanged +=
            Tree_SelectedItemChanged;

        _tree.MouseDoubleClick +=
            Tree_MouseDoubleClick;

        ScrollViewer.SetVerticalScrollBarVisibility(
            _tree,
            ScrollBarVisibility.Auto);

        PopulateTree();

        treeBorder.Child =
            _tree;

        Grid.SetRow(
            treeBorder,
            2);

        root.Children.Add(
            treeBorder);

        TextBlock issueText =
            new()
            {
                Margin =
                    new Thickness(
                        0,
                        12,
                        0,
                        0),
                Foreground =
                    MutedText,
                FontSize =
                    11,
                TextWrapping =
                    TextWrapping.Wrap,
                Text =
                    _hiddenItemCount ==
                        0
                        ? string.Empty
                        : $"{_hiddenItemCount:N0} incompatible item{(_hiddenItemCount == 1 ? string.Empty : "s")} hidden from this archive.",
                Visibility =
                    _hiddenItemCount >
                        0
                        ? Visibility.Visible
                        : Visibility.Collapsed
            };

        Grid.SetRow(
            issueText,
            3);

        root.Children.Add(
            issueText);

        _statusText.Margin =
            new Thickness(
                0,
                12,
                0,
                14);

        _statusText.Text =
            _wads.Count >
                0
                ? "Select a WAD file."
                : "No WAD could be extracted from this package.";

        _statusText.Foreground =
            MutedText;

        _statusText.FontSize =
            11;

        _statusText.TextWrapping =
            TextWrapping.Wrap;

        Grid.SetRow(
            _statusText,
            4);

        root.Children.Add(
            _statusText);

        StackPanel actionButtons =
            new()
            {
                Orientation =
                    Orientation.Horizontal,
                HorizontalAlignment =
                    HorizontalAlignment.Right
            };

        _previewButton =
            CreateButton(
                "Preview Selected",
                primary:
                    false);

        _previewButton.IsEnabled =
            false;

        _previewButton.Click +=
            PreviewButton_Click;

        actionButtons.Children.Add(
            _previewButton);

        _addSelectedButton =
            CreateButton(
                "Add Selected to Library",
                primary:
                    false);

        _addSelectedButton.IsEnabled =
            false;

        _addSelectedButton.Click +=
            AddSelectedButton_Click;

        actionButtons.Children.Add(
            _addSelectedButton);

        _importAllButton =
            CreateButton(
                "Import All to Library",
                primary:
                    true);

        _importAllButton.IsEnabled =
            _wads.Count >
            0;

        _importAllButton.Click +=
            ImportAllButton_Click;

        actionButtons.Children.Add(
            _importAllButton);

        _closeButton =
            CreateButton(
                "Close",
                primary:
                    false);

        _closeButton.Margin =
            new Thickness(
                16,
                0,
                0,
                0);

        _closeButton.Click +=
            (_, _) =>
                Close();

        actionButtons.Children.Add(
            _closeButton);

        Grid.SetRow(
            actionButtons,
            5);

        root.Children.Add(
            actionButtons);

        Content =
            root;
    }

    public int ImportedCount { get; private set; }

    private void PopulateTree()
    {
        Dictionary<string, TreeViewItem> folders =
            new(
                StringComparer.OrdinalIgnoreCase);

        foreach (CompanionOnlineWadDownloadedItem wad in
                 _wads.OrderBy(
                     item =>
                         item.ArchivePath,
                     StringComparer.OrdinalIgnoreCase))
        {
            string[] segments =
                wad.ArchivePath
                    .Replace(
                        '\\',
                        '/')
                    .Split(
                        '/',
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries);

            if (segments.Length ==
                0)
            {
                segments =
                    new[]
                    {
                        wad.FileName
                    };
            }

            ItemsControl parent =
                _tree;

            string currentPath =
                string.Empty;

            for (int index = 0;
                 index < segments.Length - 1;
                 index++)
            {
                currentPath =
                    currentPath.Length ==
                    0
                        ? segments[index]
                        : currentPath + "/" + segments[index];

                if (!folders.TryGetValue(
                        currentPath,
                        out TreeViewItem? folder))
                {
                    folder =
                        new TreeViewItem
                        {
                            Header =
                                segments[index],
                            Foreground =
                                SecondaryText,
                            FontWeight =
                                FontWeights.SemiBold,
                            IsExpanded =
                                true,
                            Margin =
                                new Thickness(
                                    0,
                                    3,
                                    0,
                                    3)
                        };

                    parent.Items.Add(
                        folder);

                    folders[currentPath] =
                        folder;
                }

                parent =
                    folder;
            }

            TreeViewItem leaf =
                new()
                {
                    Header =
                        CreateWadHeader(
                            wad),
                    Foreground =
                        PrimaryText,
                    Tag =
                        wad,
                    ToolTip =
                        wad.ArchivePath,
                    HorizontalContentAlignment =
                        HorizontalAlignment.Stretch,
                    Margin =
                        new Thickness(
                            0,
                            4,
                            0,
                            4),
                    Padding =
                        new Thickness(
                            0)
                };

            parent.Items.Add(
                leaf);
        }
    }

    private FrameworkElement CreateWadHeader(
        CompanionOnlineWadDownloadedItem wad)
    {
        WadRegistrationResult inspection =
            WadRegistrationService.Inspect(
                wad.TemporaryPath);

        CompanionRobustWadTextureCatalog? catalog =
            null;

        if (inspection.WadIsValid)
        {
            try
            {
                catalog =
                    CompanionRobustWadTextureCatalogService.ReadCatalog(
                        wad.TemporaryPath);
            }
            catch
            {
            }
        }

        string format =
            inspection.WadIsValid
                ? inspection.WadFormat
                : "Unknown";

        string palette =
            string.Equals(
                format,
                "WAD3",
                StringComparison.OrdinalIgnoreCase)
                ? "Embedded per-texture palettes"
                : string.IsNullOrWhiteSpace(
                    _sourceEntry.PaletteHint)
                    ? "External palette"
                    : _sourceEntry.PaletteHint +
                      " palette";

        string textureSummary =
            catalog is
            null
                ? "Preview index unavailable"
                : $"{catalog.Textures.Count:N0} brush textures";

        string entrySummary =
            inspection.TextureCountText +
            " archive entries";

        string warningSummary =
            catalog is not
                null &&
            catalog.SkippedMipTextureCount >
                0
                ? $" • {catalog.SkippedMipTextureCount:N0} skipped"
                : string.Empty;

        Border card =
            new()
            {
                Background =
                    CreateBrush(
                        0x13,
                        0x1A,
                        0x22),
                BorderBrush =
                    PanelBorder,
                BorderThickness =
                    new Thickness(
                        1),
                CornerRadius =
                    new CornerRadius(
                        6),
                Padding =
                    new Thickness(
                        12,
                        9,
                        12,
                        9),
                Margin =
                    new Thickness(
                        2)
            };

        StackPanel stack =
            new();

        TextBlock name =
            new()
            {
                Text =
                    wad.FileName,
                Foreground =
                    PrimaryText,
                FontSize =
                    13,
                FontWeight =
                    FontWeights.SemiBold
            };

        stack.Children.Add(
            name);

        TextBlock details =
            new()
            {
                Margin =
                    new Thickness(
                        0,
                        4,
                        0,
                        0),
                Text =
                    $"{format} • {palette} • {textureSummary} • {entrySummary}{warningSummary}",
                Foreground =
                    SecondaryText,
                FontSize =
                    11,
                TextWrapping =
                    TextWrapping.Wrap
            };

        stack.Children.Add(
            details);

        if (!string.Equals(
                wad.ArchivePath,
                wad.FileName,
                StringComparison.OrdinalIgnoreCase))
        {
            TextBlock path =
                new()
                {
                    Margin =
                        new Thickness(
                            0,
                            3,
                            0,
                            0),
                    Text =
                        wad.ArchivePath,
                    Foreground =
                        MutedText,
                    FontSize =
                        10,
                    TextWrapping =
                        TextWrapping.Wrap
                };

            stack.Children.Add(
                path);
        }

        card.Child =
            stack;

        return card;
    }

    private CompanionOnlineWadDownloadedItem? GetSelectedWad()
    {
        return _tree.SelectedItem is
            TreeViewItem selected &&
            selected.Tag is
                CompanionOnlineWadDownloadedItem wad
                ? wad
                : null;
    }

    private void Tree_SelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        CompanionOnlineWadDownloadedItem? wad =
            GetSelectedWad();

        bool selected =
            wad is not
            null;

        _previewButton.IsEnabled =
            selected &&
            !_busy;

        _addSelectedButton.IsEnabled =
            selected &&
            !_busy;

        _statusText.Text =
            wad is null
                ? "Select a WAD file."
                : wad.ArchivePath;
    }

    private async void Tree_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (GetSelectedWad() is not
            CompanionOnlineWadDownloadedItem wad ||
            _busy)
        {
            return;
        }

        await PreviewWadAsync(
            wad);

        e.Handled =
            true;
    }

    private async void PreviewButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GetSelectedWad() is not
            CompanionOnlineWadDownloadedItem wad)
        {
            return;
        }

        await PreviewWadAsync(
            wad);
    }

    private async Task PreviewWadAsync(
        CompanionOnlineWadDownloadedItem wad)
    {
        SetBusy(
            true);

        _statusText.Text =
            $"Opening {wad.ArchivePath}...";

        try
        {
            await _previewAsync(
                wad,
                this);

            _statusText.Text =
                wad.ArchivePath;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Community WAD Preview",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _statusText.Text =
                "Preview failed.";
        }
        finally
        {
            SetBusy(
                false);
        }
    }

    private async void AddSelectedButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GetSelectedWad() is not
            CompanionOnlineWadDownloadedItem wad)
        {
            return;
        }

        SetBusy(
            true);

        _statusText.Text =
            $"Importing {wad.ArchivePath}...";

        try
        {
            int completed =
                await _importAsync(
                    new[]
                    {
                        wad
                    },
                    this);

            ImportedCount +=
                completed;

            _statusText.Text =
                completed >
                0
                    ? $"{wad.ArchivePath} was added to or reused from the global library."
                    : "No WAD was imported.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Community WAD Import",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _statusText.Text =
                "Import failed.";
        }
        finally
        {
            SetBusy(
                false);
        }
    }

    private async void ImportAllButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        MessageBoxResult confirmation =
            MessageBox.Show(
                this,
                $"Import all {_wads.Count:N0} extracted WADs from this archive into the global Companion library?",
                "Import All WADs",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

        if (confirmation !=
            MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(
            true);

        _statusText.Text =
            $"Importing {_wads.Count:N0} WADs...";

        try
        {
            int completed =
                await _importAsync(
                    _wads,
                    this);

            ImportedCount +=
                completed;

            _statusText.Text =
                completed ==
                1
                    ? "1 WAD was added to or reused from the global library."
                    : $"{completed:N0} WADs were added to or reused from the global library.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Community WAD Import",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _statusText.Text =
                "Import failed.";
        }
        finally
        {
            SetBusy(
                false);
        }
    }

    private void SetBusy(
        bool busy)
    {
        _busy =
            busy;

        _tree.IsEnabled =
            !busy;

        CompanionOnlineWadDownloadedItem? selected =
            GetSelectedWad();

        _previewButton.IsEnabled =
            !busy &&
            selected is not
            null;

        _addSelectedButton.IsEnabled =
            !busy &&
            selected is not
            null;

        _importAllButton.IsEnabled =
            !busy &&
            _wads.Count >
            0;

        _closeButton.IsEnabled =
            !busy;
    }

    private static Button CreateButton(
        string text,
        bool primary)
    {
        return new Button
        {
            Content =
                text,
            MinWidth =
                128,
            MinHeight =
                38,
            Margin =
                new Thickness(
                    0,
                    0,
                    10,
                    0),
            Padding =
                new Thickness(
                    15,
                    8,
                    15,
                    8),
            Foreground =
                PrimaryText,
            Background =
                primary
                    ? PrimaryButtonBackground
                    : ButtonBackground,
            BorderBrush =
                primary
                    ? Accent
                    : PanelBorder,
            BorderThickness =
                new Thickness(
                    1),
            FontWeight =
                FontWeights.SemiBold,
            Cursor =
                Cursors.Hand
        };
    }

    private static SolidColorBrush CreateBrush(
        byte red,
        byte green,
        byte blue)
    {
        SolidColorBrush brush =
            new(
                Color.FromRgb(
                    red,
                    green,
                    blue));

        brush.Freeze();

        return brush;
    }
}
