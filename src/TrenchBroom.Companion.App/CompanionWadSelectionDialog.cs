using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrenchBroom.Companion.Core;

namespace TrenchBroom.Companion.App;

public sealed class CompanionWadSelectionDialog : Window
{
    private readonly List<CheckBox>
        _activeCheckBoxes =
            new();

    private readonly HashSet<string>
        _preservedOppositeFormatSelections;

    private readonly CheckBox
        _projectDefaultCheckBox =
            new();

    private readonly string
        _preferredWadFormat;

    public CompanionWadSelectionDialog(
        string mapName,
        string preferredFormatDisplayName,
        IReadOnlyList<CompanionWadLibraryAsset> assets,
        IReadOnlyCollection<string> selectedAssetIds,
        bool preferProjectDefault)
    {
        Title =
            $"WADs for {mapName}";

        Width =
            760;

        Height =
            650;

        MinWidth =
            760;

        MinHeight =
            650;

        MaxWidth =
            760;

        MaxHeight =
            650;

        ResizeMode =
            ResizeMode.NoResize;

        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;

        Background =
            Brush(
                0x0E,
                0x12,
                0x18);

        Foreground =
            Brushes.White;

        _preferredWadFormat =
            preferredFormatDisplayName.Contains(
                "3",
                StringComparison.OrdinalIgnoreCase)
                ? "WAD3"
                : "WAD2";

        string oppositeFormat =
            string.Equals(
                _preferredWadFormat,
                "WAD3",
                StringComparison.OrdinalIgnoreCase)
                ? "WAD2"
                : "WAD3";

        HashSet<string> selected =
            new(
                selectedAssetIds,
                StringComparer.OrdinalIgnoreCase);

        _preservedOppositeFormatSelections =
            assets
                .Where(
                    asset =>
                        string.Equals(
                            asset.WadFormat,
                            oppositeFormat,
                            StringComparison.OrdinalIgnoreCase) &&
                        selected.Contains(
                            asset.AssetId))
                .Select(
                    asset =>
                        asset.AssetId)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        Grid root =
            new()
            {
                Margin =
                    new Thickness(
                        24)
            };

        root.RowDefinitions.Add(
            AutoRow());

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(
                        14)
            });

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(
                        12)
            });

        root.RowDefinitions.Add(
            AutoRow());

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(
                        10)
            });

        root.RowDefinitions.Add(
            AutoRow());

        StackPanel header =
            new();

        header.Children.Add(
            new TextBlock
            {
                Text =
                    $"Choose WADs for {mapName}",

                FontSize =
                    22,

                FontWeight =
                    FontWeights.SemiBold,

                Foreground =
                    Brushes.White
            });

        header.Children.Add(
            new TextBlock
            {
                Text =
                    $"{_preferredWadFormat} is the active texture archive format for this project. The opposite format is unavailable here.",

                Margin =
                    new Thickness(
                        0,
                        7,
                        0,
                        0),

                Foreground =
                    Brush(
                        0xAE,
                        0xB9,
                        0xC7),

                TextWrapping =
                    TextWrapping.Wrap
            });

        Grid.SetRow(
            header,
            0);

        root.Children.Add(
            header);

        TabControl tabs =
            new()
            {
                Background =
                    Brush(
                        0x11,
                        0x18,
                        0x20),

                BorderBrush =
                    Brush(
                        0x2F,
                        0x3D,
                        0x4D),

                Foreground =
                    Brushes.White
            };

        TabItem wad2Tab =
            CreateFormatTab(
                "WAD2",
                assets,
                selected);

        TabItem wad3Tab =
            CreateFormatTab(
                "WAD3",
                assets,
                selected);

        wad2Tab.IsEnabled =
            string.Equals(
                _preferredWadFormat,
                "WAD2",
                StringComparison.OrdinalIgnoreCase);

        wad3Tab.IsEnabled =
            string.Equals(
                _preferredWadFormat,
                "WAD3",
                StringComparison.OrdinalIgnoreCase);

        tabs.Items.Add(
            wad2Tab);

        tabs.Items.Add(
            wad3Tab);

        tabs.SelectedItem =
            wad3Tab.IsEnabled
                ? wad3Tab
                : wad2Tab;

        Grid.SetRow(
            tabs,
            2);

        root.Children.Add(
            tabs);

        StackPanel defaultPanel =
            new();

        _projectDefaultCheckBox.Content =
            "Use this selection as the default for new maps";

        _projectDefaultCheckBox.IsChecked =
            preferProjectDefault;

        _projectDefaultCheckBox.Foreground =
            Brushes.White;

        _projectDefaultCheckBox.HorizontalAlignment =
            HorizontalAlignment.Left;

        defaultPanel.Children.Add(
            _projectDefaultCheckBox);

        if (_preservedOppositeFormatSelections.Count >
            0)
        {
            defaultPanel.Children.Add(
                new TextBlock
                {
                    Text =
                        $"{_preservedOppositeFormatSelections.Count:N0} existing {oppositeFormat} selection(s) are preserved but cannot be edited while this project uses {_preferredWadFormat}.",

                    Margin =
                        new Thickness(
                            0,
                            6,
                            0,
                            0),

                    Foreground =
                        Brush(
                            0xAE,
                            0xB9,
                            0xC7),

                    TextWrapping =
                        TextWrapping.Wrap
                });
        }

        Grid.SetRow(
            defaultPanel,
            4);

        root.Children.Add(
            defaultPanel);

        Grid footer =
            new();

        footer.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        footer.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        StackPanel buttons =
            new()
            {
                Orientation =
                    Orientation.Horizontal
            };

        Button selectAllButton =
            CreateButton(
                "Select All",
                primary: false);

        selectAllButton.Click +=
            (_, _) =>
            {
                foreach (CheckBox checkBox in
                         _activeCheckBoxes)
                {
                    checkBox.IsChecked =
                        true;
                }
            };

        Button clearButton =
            CreateButton(
                "Clear",
                primary: false);

        clearButton.Click +=
            (_, _) =>
            {
                foreach (CheckBox checkBox in
                         _activeCheckBoxes)
                {
                    checkBox.IsChecked =
                        false;
                }
            };

        Button cancelButton =
            CreateButton(
                "Cancel",
                primary: false);

        cancelButton.Click +=
            (_, _) =>
            {
                DialogResult =
                    false;
            };

        Button saveButton =
            CreateButton(
                "Save WADs",
                primary: true);

        saveButton.Click +=
            (_, _) =>
            {
                List<string> selectedIds =
                    _activeCheckBoxes
                        .Where(
                            checkBox =>
                                checkBox.IsChecked ==
                                true)
                        .Select(
                            checkBox =>
                                checkBox.Tag as string)
                        .Where(
                            assetId =>
                                !string.IsNullOrWhiteSpace(
                                    assetId))
                        .Cast<string>()
                        .ToList();

                foreach (string preserved in
                         _preservedOppositeFormatSelections)
                {
                    if (!selectedIds.Contains(
                            preserved,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        selectedIds.Add(
                            preserved);
                    }
                }

                SelectedAssetIds =
                    selectedIds;

                UseAsProjectDefault =
                    _projectDefaultCheckBox.IsChecked ==
                    true;

                DialogResult =
                    true;
            };

        buttons.Children.Add(
            selectAllButton);

        buttons.Children.Add(
            clearButton);

        buttons.Children.Add(
            cancelButton);

        buttons.Children.Add(
            saveButton);

        Grid.SetColumn(
            buttons,
            1);

        footer.Children.Add(
            buttons);

        Grid.SetRow(
            footer,
            6);

        root.Children.Add(
            footer);

        Content =
            root;
    }

    public IReadOnlyList<string> SelectedAssetIds { get; private set; } =
        Array.Empty<string>();

    public bool UseAsProjectDefault { get; private set; }

    private TabItem CreateFormatTab(
        string wadFormat,
        IReadOnlyList<CompanionWadLibraryAsset> assets,
        IReadOnlySet<string> selected)
    {
        List<CompanionWadLibraryAsset> formatAssets =
            assets
                .Where(
                    asset =>
                        string.Equals(
                            asset.WadFormat,
                            wadFormat,
                            StringComparison.OrdinalIgnoreCase))
                .OrderBy(
                    asset =>
                        asset.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        StackPanel list =
            new();

        if (formatAssets.Count ==
            0)
        {
            list.Children.Add(
                new TextBlock
                {
                    Text =
                        $"No {wadFormat} WADs are currently in the Companion library.",

                    Margin =
                        new Thickness(
                            8),

                    Foreground =
                        Brush(
                            0xAE,
                            0xB9,
                            0xC7),

                    TextWrapping =
                        TextWrapping.Wrap
                });
        }
        else
        {
            foreach (CompanionWadLibraryAsset asset in
                     formatAssets)
            {
                CheckBox checkBox =
                    new()
                    {
                        Tag =
                            asset.AssetId,

                        IsChecked =
                            selected.Contains(
                                asset.AssetId),

                        Margin =
                            new Thickness(
                                6,
                                5,
                                6,
                                5),

                        Foreground =
                            Brushes.White,

                        FontSize =
                            13,

                        Content =
                            $"{asset.DisplayName}    {asset.WadFormat}    {asset.TextureCount:N0} textures"
                    };

                checkBox.ToolTip =
                    asset.WadPath;

                list.Children.Add(
                    checkBox);

                if (string.Equals(
                        wadFormat,
                        _preferredWadFormat,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _activeCheckBoxes.Add(
                        checkBox);
                }
            }
        }

        ScrollViewer scroll =
            new()
            {
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,

                Content =
                    list,

                Padding =
                    new Thickness(
                        10)
            };

        return new TabItem
        {
            Header =
                $"{wadFormat} ({formatAssets.Count:N0})",

            Content =
                scroll
        };
    }

    private static RowDefinition AutoRow()
    {
        return new RowDefinition
        {
            Height =
                GridLength.Auto
        };
    }

    private static Button CreateButton(
        string text,
        bool primary)
    {
        Button button =
            new()
            {
                Content =
                    text,

                MinWidth =
                    primary
                        ? 112
                        : 84,

                Height =
                    36,

                Margin =
                    new Thickness(
                        8,
                        0,
                        0,
                        0),

                Padding =
                    new Thickness(
                        12,
                        0,
                        12,
                        0),

                Foreground =
                    Brushes.White,

                Background =
                    primary
                        ? Brush(
                            0x24,
                            0x4A,
                            0x75)
                        : Brush(
                            0x1C,
                            0x25,
                            0x30),

                BorderBrush =
                    primary
                        ? Brush(
                            0x5E,
                            0xA2,
                            0xFF)
                        : Brush(
                            0x36,
                            0x44,
                            0x55),

                BorderThickness =
                    new Thickness(
                        1)
            };

        return button;
    }

    private static SolidColorBrush Brush(
        byte red,
        byte green,
        byte blue)
    {
        return new SolidColorBrush(
            Color.FromRgb(
                red,
                green,
                blue));
    }
}
