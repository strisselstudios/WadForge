using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using TrenchBroom.Companion.Core;

namespace TrenchBroom.Companion.App;

public partial class App : Application
{
    private const string DuskCompilePrepCommand =
        "--dusk-compile-prep";

    protected override void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(
            e);

        if (e.Args.Length >
                0 &&
            string.Equals(
                e.Args[0],
                DuskCompilePrepCommand,
                StringComparison.OrdinalIgnoreCase))
        {
            int exitCode =
                RunDuskCompilePreparation(
                    e.Args);

            Shutdown(
                exitCode);

            return;
        }

        MainWindow window =
            new();

        MainWindow =
            window;

        window.Show();
    }

    private static int RunDuskCompilePreparation(
        IReadOnlyList<string> arguments)
    {
        Dictionary<string, string> options;

        try
        {
            options =
                ParseOptions(
                    arguments,
                    startIndex: 1);
        }
        catch (Exception exception)
        {
            TryWriteFallbackError(
                exception);

            return 2;
        }

        string? logPath =
            TryGet(
                options,
                "--log");

        try
        {
            string mapPath =
                GetRequired(
                    options,
                    "--map");

            string projectWads =
                GetRequired(
                    options,
                    "--project-wads");

            string cacheWads =
                GetRequired(
                    options,
                    "--cache-wads");

            string palettePath =
                GetRequired(
                    options,
                    "--palette");

            CompanionDuskCompilePreparationResult result =
                CompanionDuskCompilePreparationService.PrepareHalfLifeCompile(
                    mapPath,
                    projectWads,
                    cacheWads,
                    palettePath);

            if (!string.IsNullOrWhiteSpace(
                    logPath))
            {
                WriteLog(
                    logPath,
                    "PASS: DUSK HLBSP compile preparation" +
                    Environment.NewLine +
                    $"WAD2 archives cached: {result.Wad2ConvertedCount}" +
                    Environment.NewLine +
                    $"WAD3 archives preserved: {result.Wad3PreservedCount}" +
                    Environment.NewLine +
                    "WAD order: " +
                    string.Join(
                        ";",
                        result.OrderedWadNames) +
                    Environment.NewLine +
                    "Cache: " +
                    result.CacheWadDirectory +
                    Environment.NewLine);
            }

            return 0;
        }
        catch (Exception exception)
        {
            if (!string.IsNullOrWhiteSpace(
                    logPath))
            {
                try
                {
                    WriteLog(
                        logPath,
                        "FAIL: DUSK HLBSP compile preparation" +
                        Environment.NewLine +
                        exception +
                        Environment.NewLine);
                }
                catch
                {
                    TryWriteFallbackError(
                        exception);
                }
            }
            else
            {
                TryWriteFallbackError(
                    exception);
            }

            return 3;
        }
    }

    private static Dictionary<string, string> ParseOptions(
        IReadOnlyList<string> arguments,
        int startIndex)
    {
        Dictionary<string, string> options =
            new(
                StringComparer.OrdinalIgnoreCase);

        for (int index = startIndex;
             index < arguments.Count;
             index += 2)
        {
            string key =
                arguments[index];

            if (!key.StartsWith(
                    "--",
                    StringComparison.Ordinal) ||
                index +
                    1 >=
                arguments.Count)
            {
                throw new ArgumentException(
                    "Compile preparation arguments must be supplied as --name value pairs.");
            }

            options[key] =
                arguments[index + 1];
        }

        return options;
    }

    private static string GetRequired(
        IReadOnlyDictionary<string, string> options,
        string key)
    {
        if (!options.TryGetValue(
                key,
                out string? value) ||
            string.IsNullOrWhiteSpace(
                value))
        {
            throw new ArgumentException(
                $"Missing required compile preparation argument: {key}");
        }

        return value;
    }

    private static string? TryGet(
        IReadOnlyDictionary<string, string> options,
        string key)
    {
        return options.TryGetValue(
                key,
                out string? value)
            ? value
            : null;
    }

    private static void WriteLog(
        string path,
        string content)
    {
        string fullPath =
            Path.GetFullPath(
                path);

        string? directory =
            Path.GetDirectoryName(
                fullPath);

        if (!string.IsNullOrWhiteSpace(
                directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        File.WriteAllText(
            fullPath,
            content,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false));
    }

    private static void TryWriteFallbackError(
        Exception exception)
    {
        try
        {
            Console.Error.WriteLine(
                exception);
        }
        catch
        {
        }
    }
}
