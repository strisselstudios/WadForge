using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrenchBroom.Companion.Core;

namespace TrenchBroom.Companion.App;

public partial class CompanionBuildSettingsDialog :
    Window
{
    private sealed class OptionEditor
    {
        public required CompanionCompilerOptionDefinition
            Definition { get; init; }

        public required CheckBox
            EnabledCheckBox { get; init; }

        public TextBox?
            ValueTextBox { get; init; }

        public ComboBox?
            ThreadModeComboBox { get; init; }

        public TextBox?
            ThreadCountTextBox { get; init; }
    }

    private static readonly string[]
        CommonOptionIds =
        {
            "qbsp.bsp2",
            "light.lit",
            "light.extra",
            "light.extra4",
            "light.soft",
            "lightglobal.dirt",
            "lightglobal.bounce",
            "lightglobal.sunlight",
            "lightglobal.sunlight_mangle",
            "lightglobal.sunlight_penumbra",
            "lightglobal.gamma",
            "qbsp.leaktest",
            "light.threads"
        };

    private static readonly Dictionary<string, int>
        ToolPriority =
            new(
                StringComparer.OrdinalIgnoreCase)
            {
                ["vis.fast"] = 0,
                ["vis.level"] = 1,
                ["vis.threads"] = 2,

                ["qbsp.forcegoodtree"] = 0,
                ["qbsp.subdivide"] = 1,
                ["qbsp.maxnodesize"] = 2,

                ["light.gate"] = 0,
                ["light.sunsamples"] = 1,
                ["light.surflight_subdivide"] = 2,
                ["light.novisapprox"] = 3,

                ["lightglobal.minlight"] = 0,
                ["lightglobal.minlight_color"] = 1,
                ["lightglobal.sunlight_color"] = 2,
                ["lightglobal.sunlight2"] = 3
            };

    private readonly CompanionCompilerOptionSchema
        _schema;

    private readonly Dictionary<string, OptionEditor>
        _editors =
            new(
                StringComparer.OrdinalIgnoreCase);

    private bool
        _applyingSettings;

    public CompanionBuildSettingsDialog(
        string projectName,
        CompanionCompilerOptionSchema schema,
        CompanionBuildSettings settings)
    {
        ArgumentNullException.ThrowIfNull(
            schema);

        ArgumentNullException.ThrowIfNull(
            settings);

        InitializeComponent();

        _schema =
            schema;

        ContextText.Text =
            $"{projectName} • DUSK • ericw-tools {schema.ToolchainVersion}";

        BuildCommonPanel();

        BuildOptionPanel(
            QbspOptionsPanel,
            CompanionCompilerTool.Qbsp);

        BuildOptionPanel(
            VisOptionsPanel,
            CompanionCompilerTool.Vis);

        BuildOptionPanel(
            LightOptionsPanel,
            CompanionCompilerTool.Light);

        BuildOptionPanel(
            LightGlobalOptionsPanel,
            CompanionCompilerTool.LightGlobal);

        ApplySettings(
            settings);
    }

    public CompanionBuildSettings SelectedSettings { get; private set; } =
        new();

    private void BuildCommonPanel()
    {
        CommonOptionsPanel.Children.Add(
            CreateSectionHeader(
                "Frequently Used"));

        foreach (string optionId in
                 CommonOptionIds)
        {
            CompanionCompilerOptionDefinition? definition =
                _schema.Options.FirstOrDefault(
                    option =>
                        string.Equals(
                            option.Id,
                            optionId,
                            StringComparison.OrdinalIgnoreCase));

            if (definition is null)
            {
                continue;
            }

            AddOptionRow(
                CommonOptionsPanel,
                definition);
        }
    }

    private void BuildOptionPanel(
        Panel panel,
        CompanionCompilerTool tool)
    {
        string? previousCategory =
            null;

        IEnumerable<CompanionCompilerOptionDefinition> definitions =
            _schema.Options
                .Where(
                    option =>
                        option.Tool ==
                            tool &&
                        !IsCommonOption(
                            option.Id))
                .Select(
                    (option, index) =>
                        new
                        {
                            Option =
                                option,

                            OriginalIndex =
                                index
                        })
                .OrderBy(
                    item =>
                        GetToolPriority(
                            item.Option.Id))
                .ThenBy(
                    item =>
                        item.OriginalIndex)
                .Select(
                    item =>
                        item.Option);

        foreach (CompanionCompilerOptionDefinition definition in
                 definitions)
        {
            if (!string.Equals(
                    previousCategory,
                    definition.Category,
                    StringComparison.Ordinal))
            {
                panel.Children.Add(
                    CreateSectionHeader(
                        definition.Category));

                previousCategory =
                    definition.Category;
            }

            AddOptionRow(
                panel,
                definition);
        }
    }

    private void AddOptionRow(
        Panel panel,
        CompanionCompilerOptionDefinition definition)
    {
        OptionEditor editor =
            CreateOptionEditor(
                definition);

        _editors[definition.Id] =
            editor;

        panel.Children.Add(
            (UIElement)editor.EnabledCheckBox.Tag);
    }

    private static bool IsCommonOption(
        string optionId)
    {
        return CommonOptionIds.Any(
            candidate =>
                string.Equals(
                    candidate,
                    optionId,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static int GetToolPriority(
        string optionId)
    {
        return ToolPriority.TryGetValue(
                optionId,
                out int priority)
            ? priority
            : 100;
    }

    private static TextBlock CreateSectionHeader(
        string category)
    {
        return new TextBlock
        {
            Margin =
                new Thickness(
                    2,
                    10,
                    2,
                    5),

            FontSize =
                13,

            FontWeight =
                FontWeights.SemiBold,

            Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        190,
                        203,
                        219)),

            Text =
                category.ToUpperInvariant()
        };
    }

    private OptionEditor CreateOptionEditor(
        CompanionCompilerOptionDefinition definition)
    {
        Border row =
            new()
            {
                Padding =
                    new Thickness(
                        7,
                        6,
                        5,
                        6),

                BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(
                            44,
                            57,
                            74)),

                BorderThickness =
                    new Thickness(
                        0,
                        0,
                        0,
                        1),

                Background =
                    Brushes.Transparent
            };

        Grid grid =
            new();

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        28)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        175)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        38)
            });

        row.Child =
            grid;

        CheckBox enabled =
            new()
            {
                VerticalAlignment =
                    VerticalAlignment.Center,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                IsEnabled =
                    definition.Available,

                Tag =
                    row
            };

        enabled.Checked +=
            OptionCheckBox_Changed;

        enabled.Unchecked +=
            OptionCheckBox_Changed;

        Grid.SetColumn(
            enabled,
            0);

        grid.Children.Add(
            enabled);

        TextBlock flagText =
            new()
            {
                VerticalAlignment =
                    VerticalAlignment.Center,

                FontFamily =
                    new FontFamily(
                        "Consolas"),

                FontWeight =
                    FontWeights.Bold,

                FontSize =
                    13,

                Foreground =
                    definition.Available
                        ? Brushes.White
                        : new SolidColorBrush(
                            Color.FromRgb(
                                143,
                                157,
                                175)),

                Text =
                    definition.Flag
            };

        Grid.SetColumn(
            flagText,
            1);

        grid.Children.Add(
            flagText);

        TextBlock titleText =
            new()
            {
                Margin =
                    new Thickness(
                        6,
                        0,
                        8,
                        0),

                VerticalAlignment =
                    VerticalAlignment.Center,

                Foreground =
                    definition.Available
                        ? new SolidColorBrush(
                            Color.FromRgb(
                                221,
                                229,
                                239))
                        : new SolidColorBrush(
                            Color.FromRgb(
                                143,
                                157,
                                175)),

                Text =
                    definition.Available
                        ? definition.DisplayName
                        : definition.DisplayName +
                          "  ·  locked"
            };

        Grid.SetColumn(
            titleText,
            2);

        grid.Children.Add(
            titleText);

        TextBox? valueTextBox =
            null;

        ComboBox? threadModeComboBox =
            null;

        TextBox? threadCountTextBox =
            null;

        if (definition.ValueKind ==
            CompanionCompilerOptionValueKind.Threads)
        {
            StackPanel threadPanel =
                new()
                {
                    Orientation =
                        Orientation.Horizontal,

                    Margin =
                        new Thickness(
                            6,
                            0,
                            5,
                            0),

                    VerticalAlignment =
                        VerticalAlignment.Center
                };

            threadModeComboBox =
                new ComboBox
                {
                    Width =
                        132,

                    Foreground =
                        Brushes.White
                };

            threadModeComboBox.Items.Add(
                new ComboBoxItem
                {
                    Content =
                        "Automatic",

                    Tag =
                        CompanionBuildSettingValues.AutomaticThreads,

                    Foreground =
                        Brushes.White
                });

            threadModeComboBox.Items.Add(
                new ComboBoxItem
                {
                    Content =
                        "Custom",

                    Tag =
                        "custom",

                    Foreground =
                        Brushes.White
                });

            threadModeComboBox.SelectionChanged +=
                ThreadMode_SelectionChanged;

            threadCountTextBox =
                new TextBox
                {
                    Width =
                        54,

                    Margin =
                        new Thickness(
                            6,
                            0,
                            0,
                            0),

                    Foreground =
                        Brushes.White,

                    Text =
                        "1"
                };

            threadPanel.Children.Add(
                threadModeComboBox);

            threadPanel.Children.Add(
                threadCountTextBox);

            Grid.SetColumn(
                threadPanel,
                3);

            grid.Children.Add(
                threadPanel);
        }
        else if (definition.ValueKind !=
                 CompanionCompilerOptionValueKind.Flag)
        {
            valueTextBox =
                new TextBox
                {
                    Width =
                        175,

                    Margin =
                        new Thickness(
                            6,
                            0,
                            5,
                            0),

                    VerticalAlignment =
                        VerticalAlignment.Center,

                    Foreground =
                        Brushes.White,

                    Text =
                        definition.DefaultValue
                };

            Grid.SetColumn(
                valueTextBox,
                3);

            grid.Children.Add(
                valueTextBox);
        }

        Button infoButton =
            new()
            {
                Width =
                    27,

                Height =
                    27,

                Margin =
                    new Thickness(
                        4,
                        0,
                        0,
                        0),

                VerticalAlignment =
                    VerticalAlignment.Center,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                Foreground =
                    Brushes.White,

                Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            36,
                            50,
                            68)),

                BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(
                            96,
                            116,
                            141)),

                BorderThickness =
                    new Thickness(
                        1),

                FontWeight =
                    FontWeights.Bold,

                Content =
                    "i",

                ToolTip =
                    $"About {definition.Flag}",

                Tag =
                    definition
            };

        infoButton.Click +=
            InfoButton_Click;

        Grid.SetColumn(
            infoButton,
            4);

        grid.Children.Add(
            infoButton);

        return new OptionEditor
        {
            Definition =
                definition,

            EnabledCheckBox =
                enabled,

            ValueTextBox =
                valueTextBox,

            ThreadModeComboBox =
                threadModeComboBox,

            ThreadCountTextBox =
                threadCountTextBox
        };
    }

    private void ApplySettings(
        CompanionBuildSettings settings)
    {
        _applyingSettings =
            true;

        try
        {
            foreach (OptionEditor editor in
                     _editors.Values)
            {
                CompanionCompilerOptionSetting setting =
                    settings.Options.TryGetValue(
                        editor.Definition.Id,
                        out CompanionCompilerOptionSetting? existing)
                        ? existing
                        : new CompanionCompilerOptionSetting
                        {
                            Enabled =
                                editor.Definition.EnabledByDefault,

                            Value =
                                editor.Definition.DefaultValue
                        };

                editor.EnabledCheckBox.IsChecked =
                    editor.Definition.Available &&
                    setting.Enabled;

                if (editor.ValueTextBox is not null)
                {
                    editor.ValueTextBox.Text =
                        string.IsNullOrWhiteSpace(
                            setting.Value)
                            ? editor.Definition.DefaultValue
                            : setting.Value;
                }

                if (editor.ThreadModeComboBox is not null &&
                    editor.ThreadCountTextBox is not null)
                {
                    if (string.Equals(
                            setting.Value,
                            CompanionBuildSettingValues.AutomaticThreads,
                            StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrWhiteSpace(
                            setting.Value))
                    {
                        editor.ThreadModeComboBox.SelectedIndex =
                            0;

                        editor.ThreadCountTextBox.Text =
                            "1";
                    }
                    else
                    {
                        editor.ThreadModeComboBox.SelectedIndex =
                            1;

                        editor.ThreadCountTextBox.Text =
                            setting.Value;
                    }
                }

                RefreshEditorEnabledState(
                    editor);
            }
        }
        finally
        {
            _applyingSettings =
                false;
        }
    }

    private void RestoreDefaults_Click(
        object sender,
        RoutedEventArgs e)
    {
        ApplySettings(
            CompanionBuildSettingsService.CreateDefaults(
                _schema));
    }

    private void Documentation_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        _schema.DocumentationUrl,

                    UseShellExecute =
                        true
                });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Could not open the official ericw-tools documentation.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "EricW Reference",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void InfoButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not
                Button button ||
            button.Tag is not
                CompanionCompilerOptionDefinition definition)
        {
            return;
        }

        InfoTitleText.Text =
            $"{definition.Flag}   {definition.DisplayName}";

        InfoDescriptionText.Text =
            definition.Description;

        if (!definition.Available &&
            !string.IsNullOrWhiteSpace(
                definition.AvailabilityNote))
        {
            InfoAvailabilityText.Text =
                definition.AvailabilityNote;

            InfoAvailabilityText.Visibility =
                Visibility.Visible;
        }
        else
        {
            InfoAvailabilityText.Text =
                string.Empty;

            InfoAvailabilityText.Visibility =
                Visibility.Collapsed;
        }

        InfoPanel.Visibility =
            Visibility.Visible;
    }

    private void CloseInfo_Click(
        object sender,
        RoutedEventArgs e)
    {
        InfoPanel.Visibility =
            Visibility.Collapsed;
    }

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            CompanionBuildSettings candidate =
                ReadSettings();

            CompanionBuildSettingsService.ValidateForSave(
                candidate,
                _schema);

            SelectedSettings =
                candidate;

            DialogResult =
                true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Compile Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private CompanionBuildSettings ReadSettings()
    {
        CompanionBuildSettings settings =
            new();

        foreach (OptionEditor editor in
                 _editors.Values)
        {
            string value =
                editor.Definition.DefaultValue;

            if (editor.Definition.ValueKind ==
                CompanionCompilerOptionValueKind.Threads)
            {
                if (editor.ThreadModeComboBox?.SelectedItem is
                        ComboBoxItem selected &&
                    selected.Tag is
                        string mode &&
                    string.Equals(
                        mode,
                        "custom",
                        StringComparison.OrdinalIgnoreCase))
                {
                    value =
                        editor.ThreadCountTextBox?.Text.Trim() ??
                        string.Empty;
                }
                else
                {
                    value =
                        CompanionBuildSettingValues.AutomaticThreads;
                }
            }
            else if (editor.ValueTextBox is not null)
            {
                value =
                    editor.ValueTextBox.Text.Trim();
            }

            settings.Options[editor.Definition.Id] =
                new CompanionCompilerOptionSetting
                {
                    Enabled =
                        editor.Definition.Available &&
                        editor.EnabledCheckBox.IsChecked ==
                            true,

                    Value =
                        value
                };
        }

        return settings;
    }

    private void OptionCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not
            CheckBox checkBox)
        {
            return;
        }

        OptionEditor? editor =
            _editors.Values.FirstOrDefault(
                candidate =>
                    ReferenceEquals(
                        candidate.EnabledCheckBox,
                        checkBox));

        if (editor is null)
        {
            return;
        }

        if (!_applyingSettings &&
            checkBox.IsChecked ==
                true &&
            !string.IsNullOrWhiteSpace(
                editor.Definition.ExclusiveGroup))
        {
            _applyingSettings =
                true;

            try
            {
                foreach (OptionEditor other in
                         _editors.Values)
                {
                    if (ReferenceEquals(
                            other,
                            editor))
                    {
                        continue;
                    }

                    if (string.Equals(
                            other.Definition.ExclusiveGroup,
                            editor.Definition.ExclusiveGroup,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        other.EnabledCheckBox.IsChecked =
                            false;

                        RefreshEditorEnabledState(
                            other);
                    }
                }
            }
            finally
            {
                _applyingSettings =
                    false;
            }
        }

        RefreshEditorEnabledState(
            editor);
    }

    private void ThreadMode_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        OptionEditor? editor =
            _editors.Values.FirstOrDefault(
                candidate =>
                    ReferenceEquals(
                        candidate.ThreadModeComboBox,
                        sender));

        if (editor is not null)
        {
            RefreshEditorEnabledState(
                editor);
        }
    }

    private static void RefreshEditorEnabledState(
        OptionEditor editor)
    {
        bool optionEnabled =
            editor.Definition.Available &&
            editor.EnabledCheckBox.IsChecked ==
                true;

        if (editor.ValueTextBox is not null)
        {
            editor.ValueTextBox.IsEnabled =
                optionEnabled;
        }

        if (editor.ThreadModeComboBox is not null)
        {
            editor.ThreadModeComboBox.IsEnabled =
                optionEnabled;
        }

        if (editor.ThreadCountTextBox is not null)
        {
            bool custom =
                editor.ThreadModeComboBox?.SelectedItem is
                    ComboBoxItem selected &&
                selected.Tag is
                    string mode &&
                string.Equals(
                    mode,
                    "custom",
                    StringComparison.OrdinalIgnoreCase);

            editor.ThreadCountTextBox.IsEnabled =
                optionEnabled &&
                custom;
        }
    }
}
