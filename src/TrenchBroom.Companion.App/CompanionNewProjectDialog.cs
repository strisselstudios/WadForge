using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrenchBroom.Companion.Core;

namespace TrenchBroom.Companion.App;

internal sealed class CompanionNewProjectDialog :
    Window
{
    private readonly CompanionGameProfile _gameProfile;

    private readonly TextBox _projectNameTextBox;

    private readonly ComboBox _driveComboBox;

    private readonly ComboBox? _textureFormatComboBox;

    private readonly CheckBox _createFirstMapCheckBox;

    private readonly TextBox _firstMapNameTextBox;

    private readonly TextBlock _targetPathText;

    private readonly string? _preferredDriveRoot;

    public CompanionNewProjectDialog(
        CompanionGameProfile gameProfile,
        string? preferredDriveRoot = null)
    {
        _gameProfile =
            gameProfile ??
            throw new ArgumentNullException(
                nameof(gameProfile));

        _preferredDriveRoot =
            preferredDriveRoot;

        Title =
            "New Project";

        Width =
            560;

        SizeToContent =
            SizeToContent.Height;

        ResizeMode =
            ResizeMode.NoResize;

        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;

        Background =
            new SolidColorBrush(
                Color.FromRgb(
                    18,
                    21,
                    26));

        Foreground =
            Brushes.White;

        Grid root =
            new()
            {
                Margin =
                    new Thickness(
                        22)
            };

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        TextBlock heading =
            new()
            {
                Text =
                    $"New {_gameProfile.DisplayName} Project",

                FontSize =
                    22,

                FontWeight =
                    FontWeights.SemiBold,

                Foreground =
                    Brushes.White
            };

        Grid.SetRow(
            heading,
            0);

        root.Children.Add(
            heading);

        StackPanel namePanel =
            new()
            {
                Margin =
                    new Thickness(
                        0,
                        18,
                        0,
                        0)
            };

        namePanel.Children.Add(
            new TextBlock
            {
                Text =
                    "Project name",

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            190,
                            198,
                            209))
            });

        _projectNameTextBox =
            new TextBox
            {
                Margin =
                    new Thickness(
                        0,
                        6,
                        0,
                        0),

                Padding =
                    new Thickness(
                        9,
                        7,
                        9,
                        7),

                FontSize =
                    14
            };

        _projectNameTextBox.TextChanged +=
            ProjectNameTextBox_TextChanged;

        namePanel.Children.Add(
            _projectNameTextBox);

        Grid.SetRow(
            namePanel,
            1);

        root.Children.Add(
            namePanel);

        StackPanel drivePanel =
            new()
            {
                Margin =
                    new Thickness(
                        0,
                        14,
                        0,
                        0)
            };

        drivePanel.Children.Add(
            new TextBlock
            {
                Text =
                    "Project drive",

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            190,
                            198,
                            209))
            });

        _driveComboBox =
            new ComboBox
            {
                Margin =
                    new Thickness(
                        0,
                        6,
                        0,
                        0),

                Height =
                    36,

                VerticalContentAlignment =
                    VerticalAlignment.Center
            };

        _driveComboBox.SelectionChanged +=
            DriveComboBox_SelectionChanged;

        PopulateDrives();

        drivePanel.Children.Add(
            _driveComboBox);

        Grid.SetRow(
            drivePanel,
            2);

        root.Children.Add(
            drivePanel);

        StackPanel textureFormatPanel =
            new()
            {
                Margin =
                    new Thickness(
                        0,
                        14,
                        0,
                        0)
            };

        textureFormatPanel.Children.Add(
            new TextBlock
            {
                Text =
                    "Texture archive format",

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            190,
                            198,
                            209))
            });

        if (_gameProfile.CanChooseTextureArchiveFormat)
        {
            _textureFormatComboBox =
                new ComboBox
                {
                    Margin =
                        new Thickness(
                            0,
                            6,
                            0,
                            0),

                    Height =
                        36,

                    VerticalContentAlignment =
                        VerticalAlignment.Center
                };

            foreach (string format in
                     _gameProfile.SupportedTextureArchiveFormats)
            {
                ComboBoxItem item =
                    new()
                    {
                        Content =
                            BuildTextureFormatLabel(
                                format),

                        Tag =
                            format
                    };

                _textureFormatComboBox.Items.Add(
                    item);

                if (string.Equals(
                        format,
                        _gameProfile.DefaultTextureArchiveFormat,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _textureFormatComboBox.SelectedItem =
                        item;
                }
            }

            textureFormatPanel.Children.Add(
                _textureFormatComboBox);
        }
        else
        {
            _textureFormatComboBox =
                null;

            textureFormatPanel.Children.Add(
                new TextBlock
                {
                    Margin =
                        new Thickness(
                            0,
                            6,
                            0,
                            0),

                    Text =
                        $"{CompanionTextureArchiveFormats.GetDisplayName(_gameProfile.DefaultTextureArchiveFormat)} — fixed for {_gameProfile.DisplayName}",

                    FontWeight =
                        FontWeights.SemiBold,

                    Foreground =
                        Brushes.White
                });
        }

        textureFormatPanel.Children.Add(
            new TextBlock
            {
                Margin =
                    new Thickness(
                        0,
                        5,
                        0,
                        0),

                Text =
                    _gameProfile.CanChooseTextureArchiveFormat
                        ? "DUSK can use WAD2 or WAD3. This sets the default format Companion creates; imported WADs are detected independently."
                        : "Companion uses the normal texture archive format for this game.",

                TextWrapping =
                    TextWrapping.Wrap,

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            135,
                            145,
                            157))
            });

        Grid.SetRow(
            textureFormatPanel,
            3);

        root.Children.Add(
            textureFormatPanel);

        StackPanel firstMapPanel =
            new()
            {
                Margin =
                    new Thickness(
                        0,
                        14,
                        0,
                        0)
            };

        firstMapPanel.Children.Add(
            new TextBlock
            {
                Text =
                    "First map",

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            190,
                            198,
                            209))
            });

        StackPanel firstMapControls =
            new()
            {
                Margin =
                    new Thickness(
                        0,
                        6,
                        0,
                        0),

                Orientation =
                    Orientation.Horizontal
            };

        _createFirstMapCheckBox =
            new CheckBox
            {
                Content =
                    "Create first map",

                IsChecked =
                    true,

                VerticalAlignment =
                    VerticalAlignment.Center,

                Foreground =
                    Brushes.White
            };

        _firstMapNameTextBox =
            new TextBox
            {
                Text =
                    "map01",

                Margin =
                    new Thickness(
                        16,
                        0,
                        0,
                        0),

                Padding =
                    new Thickness(
                        9,
                        7,
                        9,
                        7),

                Width =
                    190,

                FontSize =
                    14
            };

        _createFirstMapCheckBox.Checked +=
            (_, _) =>
            {
                _firstMapNameTextBox.IsEnabled =
                    true;
            };

        _createFirstMapCheckBox.Unchecked +=
            (_, _) =>
            {
                _firstMapNameTextBox.IsEnabled =
                    false;
            };

        firstMapControls.Children.Add(
            _createFirstMapCheckBox);

        firstMapControls.Children.Add(
            _firstMapNameTextBox);

        firstMapPanel.Children.Add(
            firstMapControls);

        firstMapPanel.Children.Add(
            new TextBlock
            {
                Margin =
                    new Thickness(
                        0,
                        5,
                        0,
                        0),

                Text =
                    "The first map becomes the project's current map.",

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            135,
                            145,
                            157))
            });

        Grid.SetRow(
            firstMapPanel,
            4);

        root.Children.Add(
            firstMapPanel);

        StackPanel targetPanel =
            new()
            {
                Margin =
                    new Thickness(
                        0,
                        14,
                        0,
                        0)
            };

        targetPanel.Children.Add(
            new TextBlock
            {
                Text =
                    "Project files",

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            190,
                            198,
                            209))
            });

        _targetPathText =
            new TextBlock
            {
                Margin =
                    new Thickness(
                        0,
                        5,
                        0,
                        0),

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            135,
                            145,
                            157)),

                TextWrapping =
                    TextWrapping.Wrap
            };

        targetPanel.Children.Add(
            _targetPathText);

        Grid.SetRow(
            targetPanel,
            5);

        root.Children.Add(
            targetPanel);

        StackPanel buttons =
            new()
            {
                Margin =
                    new Thickness(
                        0,
                        22,
                        0,
                        0),

                Orientation =
                    Orientation.Horizontal,

                HorizontalAlignment =
                    HorizontalAlignment.Right
            };

        Button cancelButton =
            CreateButton(
                "Cancel");

        cancelButton.Click +=
            (_, _) =>
            {
                DialogResult =
                    false;
            };

        Button createButton =
            CreateButton(
                "Create Project");

        createButton.Margin =
            new Thickness(
                10,
                0,
                0,
                0);

        createButton.Background =
            new SolidColorBrush(
                Color.FromRgb(
                    148,
                    106,
                    24));

        createButton.BorderBrush =
            new SolidColorBrush(
                Color.FromRgb(
                    215,
                    164,
                    58));

        createButton.Click +=
            CreateButton_Click;

        buttons.Children.Add(
            cancelButton);

        buttons.Children.Add(
            createButton);

        Grid.SetRow(
            buttons,
            6);

        root.Children.Add(
            buttons);

        Content =
            root;

        UpdateTargetPath();

        Loaded +=
            (_, _) =>
            {
                _projectNameTextBox.Focus();
            };
    }

    public string ProjectName { get; private set; } =
        string.Empty;

    public string SelectedDriveRoot { get; private set; } =
        string.Empty;

    public string SelectedTextureArchiveFormat { get; private set; } =
        string.Empty;

    public bool CreateFirstMap { get; private set; } =
        true;

    public string FirstMapName { get; private set; } =
        "map01";

    private void PopulateDrives()
    {
        DriveInfo[] drives =
            DriveInfo.GetDrives()
                .Where(
                    drive =>
                        drive.IsReady &&
                        drive.DriveType is
                            DriveType.Fixed or
                            DriveType.Removable)
                .OrderBy(
                    drive =>
                        drive.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        string? applicationDrive =
            Path.GetPathRoot(
                AppContext.BaseDirectory);

        ComboBoxItem? rememberedItem =
            null;

        ComboBoxItem? applicationItem =
            null;

        foreach (DriveInfo drive in drives)
        {
            string freeSpace =
                FormatBytes(
                    drive.AvailableFreeSpace);

            ComboBoxItem item =
                new()
                {
                    Content =
                        $"{drive.Name}  ({freeSpace} free)",

                    Tag =
                        drive.RootDirectory.FullName
                };

            _driveComboBox.Items.Add(
                item);

            if (!string.IsNullOrWhiteSpace(
                    _preferredDriveRoot) &&
                string.Equals(
                    drive.RootDirectory.FullName,
                    _preferredDriveRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                rememberedItem =
                    item;
            }

            if (!string.IsNullOrWhiteSpace(
                    applicationDrive) &&
                string.Equals(
                    drive.RootDirectory.FullName,
                    applicationDrive,
                    StringComparison.OrdinalIgnoreCase))
            {
                applicationItem =
                    item;
            }
        }

        _driveComboBox.SelectedItem =
            rememberedItem ??
            applicationItem ??
            (
                _driveComboBox.Items.Count > 0
                    ? _driveComboBox.Items[0]
                    : null
            );
    }

    private static string BuildTextureFormatLabel(
        string format)
    {
        return CompanionTextureArchiveFormats.Normalize(
            format) switch
        {
            CompanionTextureArchiveFormats.Wad2 =>
                "WAD2 — Quake-style",

            CompanionTextureArchiveFormats.Wad3 =>
                "WAD3 — Half-Life-style",

            _ =>
                CompanionTextureArchiveFormats.GetDisplayName(
                    format)
        };
    }

    private string GetSelectedTextureArchiveFormat()
    {
        if (_textureFormatComboBox?.SelectedItem is
            ComboBoxItem item &&
            item.Tag is string selectedFormat)
        {
            return CompanionTextureArchiveFormats.Normalize(
                selectedFormat);
        }

        return _gameProfile.DefaultTextureArchiveFormat;
    }

    private static string FormatBytes(
        long bytes)
    {
        const double Gigabyte =
            1024d *
            1024d *
            1024d;

        return
            $"{bytes / Gigabyte:N1} GB";
    }

    private static Button CreateButton(
        string text)
    {
        return new Button
        {
            Content =
                text,

            Padding =
                new Thickness(
                    15,
                    8,
                    15,
                    8),

            MinHeight =
                38,

            MinWidth =
                100,

            Foreground =
                Brushes.White,

            Background =
                new SolidColorBrush(
                    Color.FromRgb(
                        48,
                        56,
                        66)),

            BorderBrush =
                new SolidColorBrush(
                    Color.FromRgb(
                        86,
                        97,
                        110)),

            BorderThickness =
                new Thickness(
                    1),

            FontWeight =
                FontWeights.SemiBold
        };
    }

    private void ProjectNameTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        UpdateTargetPath();
    }

    private void DriveComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateTargetPath();
    }

    private void UpdateTargetPath()
    {
        if (_targetPathText is null)
        {
            return;
        }

        string? driveRoot =
            GetSelectedDriveRoot();

        if (string.IsNullOrWhiteSpace(
                driveRoot))
        {
            _targetPathText.Text =
                "No writable project drive is available.";

            return;
        }

        string projectName =
            string.IsNullOrWhiteSpace(
                _projectNameTextBox?.Text)
                ? "Project"
                : _projectNameTextBox.Text;

        string safeProjectName;

        try
        {
            safeProjectName =
                CompanionProjectLayout
                    .SanitizeProjectDirectoryName(
                        projectName);
        }
        catch
        {
            safeProjectName =
                "Project";
        }

        _targetPathText.Text =
            Path.Combine(
                driveRoot,
                CompanionProjectLayout.WorkspaceDirectoryName,
                safeProjectName);
    }

    private string? GetSelectedDriveRoot()
    {
        if (_driveComboBox.SelectedItem is not
            ComboBoxItem item)
        {
            return null;
        }

        return item.Tag
            as string;
    }

    private void CreateButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        string projectName =
            _projectNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                projectName))
        {
            MessageBox.Show(
                this,
                "Enter a project name.",
                "Project Name Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _projectNameTextBox.Focus();
            return;
        }

        string? selectedDrive =
            GetSelectedDriveRoot();

        if (string.IsNullOrWhiteSpace(
                selectedDrive))
        {
            MessageBox.Show(
                this,
                "Choose a drive for the project files.",
                "Project Drive Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        bool createFirstMap =
            _createFirstMapCheckBox.IsChecked ==
            true;

        string firstMapName =
            _firstMapNameTextBox.Text.Trim();

        if (createFirstMap)
        {
            try
            {
                _ =
                    CompanionProjectMapCreationService
                        .BuildMapFileName(
                            firstMapName);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "First Map Name",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                _firstMapNameTextBox.Focus();
                _firstMapNameTextBox.SelectAll();
                return;
            }
        }

        ProjectName =
            projectName;

        SelectedDriveRoot =
            selectedDrive;

        SelectedTextureArchiveFormat =
            GetSelectedTextureArchiveFormat();

        CreateFirstMap =
            createFirstMap;

        FirstMapName =
            firstMapName;

        DialogResult =
            true;
    }
}