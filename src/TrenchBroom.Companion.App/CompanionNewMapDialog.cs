using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrenchBroom.Companion.Core;

namespace TrenchBroom.Companion.App;

internal sealed class CompanionNewMapDialog :
    Window
{
    private readonly string _projectDirectory;

    private readonly TextBox _mapNameTextBox;

    private readonly TextBlock _targetPathText;

    public CompanionNewMapDialog(
        string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(
                projectDirectory))
        {
            throw new ArgumentException(
                "Project directory cannot be empty.",
                nameof(projectDirectory));
        }

        _projectDirectory =
            Path.GetFullPath(
                projectDirectory);

        Title =
            "New Map";

        Width =
            500;

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

        TextBlock heading =
            new()
            {
                Text =
                    "Create New Map",

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
                    "Map name",

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            190,
                            198,
                            209))
            });

        _mapNameTextBox =
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

        _mapNameTextBox.TextChanged +=
            MapNameTextBox_TextChanged;

        namePanel.Children.Add(
            _mapNameTextBox);

        namePanel.Children.Add(
            new TextBlock
            {
                Text =
                    "Example: level01 or courtyard. Spaces become underscores.",

                Margin =
                    new Thickness(
                        0,
                        6,
                        0,
                        0),

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            135,
                            145,
                            157))
            });

        Grid.SetRow(
            namePanel,
            1);

        root.Children.Add(
            namePanel);

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
                    "Project map file",

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
            2);

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
                "Create Map");

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
            3);

        root.Children.Add(
            buttons);

        Content =
            root;

        Loaded +=
            (_, _) =>
            {
                _mapNameTextBox.Focus();
            };

        UpdateTargetPath();
    }

    public string MapName { get; private set; } =
        string.Empty;

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

    private void MapNameTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        UpdateTargetPath();
    }

    private void UpdateTargetPath()
    {
        string enteredName =
            _mapNameTextBox?.Text ??
            string.Empty;

        if (string.IsNullOrWhiteSpace(
                enteredName))
        {
            _targetPathText.Text =
                Path.Combine(
                    _projectDirectory,
                    CompanionProjectLayout.MapsDirectoryName,
                    "map-name.map");

            return;
        }

        try
        {
            string fileName =
                CompanionProjectMapCreationService.BuildMapFileName(
                    enteredName);

            _targetPathText.Text =
                Path.Combine(
                    _projectDirectory,
                    CompanionProjectLayout.MapsDirectoryName,
                    fileName);
        }
        catch
        {
            _targetPathText.Text =
                "Enter a valid map name.";
        }
    }

    private void CreateButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        string mapName =
            _mapNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                mapName))
        {
            MessageBox.Show(
                this,
                "Enter a map name.",
                "Map Name Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _mapNameTextBox.Focus();
            return;
        }

        try
        {
            _ =
                CompanionProjectMapCreationService.BuildMapFileName(
                    mapName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Invalid Map Name",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _mapNameTextBox.Focus();
            return;
        }

        MapName =
            mapName;

        DialogResult =
            true;
    }
}
