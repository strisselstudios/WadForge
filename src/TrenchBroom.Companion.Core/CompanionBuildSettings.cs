using System;
using System.Collections.Generic;

namespace TrenchBroom.Companion.Core;

public sealed class CompanionCompilerOptionSetting
{
    public bool Enabled { get; set; }
    public string Value { get; set; } = string.Empty;

    public CompanionCompilerOptionSetting Clone() =>
        new() { Enabled = Enabled, Value = Value };
}

public sealed class CompanionBuildSettings
{
    public Dictionary<string, CompanionCompilerOptionSetting> Options { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public CompanionBuildSettings Clone()
    {
        CompanionBuildSettings clone = new();
        foreach (KeyValuePair<string, CompanionCompilerOptionSetting> pair in Options)
        {
            clone.Options[pair.Key] = pair.Value.Clone();
        }
        return clone;
    }

    public CompanionCompilerOptionSetting GetOrCreate(string optionId)
    {
        if (!Options.TryGetValue(optionId, out CompanionCompilerOptionSetting? setting))
        {
            setting = new CompanionCompilerOptionSetting();
            Options[optionId] = setting;
        }
        return setting;
    }

    public bool IsEnabled(string optionId) =>
        Options.TryGetValue(optionId, out CompanionCompilerOptionSetting? setting) && setting.Enabled;
}

public static class CompanionBuildSettingValues
{
    public const string AutomaticThreads = "auto";
}
