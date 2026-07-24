using System.Diagnostics;
using Microsoft.Win32;

namespace SteelSeriesAutoEq.Services;

/// <summary>
/// Locates and starts the SteelSeries GG application when it isn't already running.
/// Sonar's local API only exists while GG is up, so this lets the app recover on its own
/// instead of just reporting "SteelSeries GG not found".
/// </summary>
public sealed class SteelSeriesLauncher
{
    // Where GG usually lives, relative to the various Program Files roots.
    private static readonly string[] RelativeInstallPaths =
    [
        @"SteelSeries\GG\SteelSeriesGG.exe",
        @"SteelSeries\SteelSeries GG\SteelSeriesGG.exe",
        @"SteelSeries\Engine 3\SteelSeriesEngine3.exe"
    ];

    private readonly AppLogger _logger;

    public SteelSeriesLauncher(AppLogger logger)
    {
        _logger = logger;
    }

    /// <summary>True when a SteelSeries GG / Sonar host process is currently running.</summary>
    public bool IsRunning()
    {
        foreach (var name in SteelSeriesHosts.ProcessNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            try
            {
                if (processes.Length > 0)
                {
                    return true;
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Starts SteelSeries GG if it can be found on disk. Returns false when it's already
    /// running or no executable could be located.
    /// </summary>
    public bool TryLaunch()
    {
        if (IsRunning())
        {
            return false;
        }

        var (path, arguments) = FindLaunchCommand();
        if (path is null)
        {
            _logger.Warn("Could not locate the SteelSeries GG executable to auto-start it.");
            return false;
        }

        try
        {
            _logger.Info($"Auto-starting SteelSeries GG: {path}");
            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty
            };

            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to start SteelSeries GG.", ex);
            return false;
        }
    }

    /// <summary>
    /// Finds the GG launch command, preferring the exact command Windows uses to auto-start it
    /// (which normally includes a "start minimized" flag), then falling back to known install paths.
    /// </summary>
    private (string? Path, string? Arguments) FindLaunchCommand()
    {
        var fromRegistry = FindFromRunKeys();
        if (fromRegistry.Path is not null)
        {
            return fromRegistry;
        }

        var installLocation = FindInstallLocationFromUninstall();
        if (installLocation is not null)
        {
            var exe = Path.Combine(installLocation, "SteelSeriesGG.exe");
            if (File.Exists(exe))
            {
                return (exe, null);
            }
        }

        foreach (var root in ProgramFilesRoots())
        {
            foreach (var relative in RelativeInstallPaths)
            {
                var candidate = Path.Combine(root, relative);
                if (File.Exists(candidate))
                {
                    return (candidate, null);
                }
            }
        }

        return (null, null);
    }

    private static (string? Path, string? Arguments) FindFromRunKeys()
    {
        (RegistryKey Root, string SubKey)[] runKeys =
        [
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run"),
            (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run"),
            (Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run")
        ];

        foreach (var (root, subKey) in runKeys)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                if (key is null)
                {
                    continue;
                }

                foreach (var valueName in key.GetValueNames())
                {
                    if (key.GetValue(valueName) is not string command || string.IsNullOrWhiteSpace(command))
                    {
                        continue;
                    }

                    if (!command.Contains("SteelSeries", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var (path, arguments) = SplitCommandLine(command);
                    if (path is not null && File.Exists(path))
                    {
                        return (path, arguments);
                    }
                }
            }
            catch
            {
                // A key may be missing or access-denied; just try the next one.
            }
        }

        return (null, null);
    }

    private static string? FindInstallLocationFromUninstall()
    {
        (RegistryKey Root, string SubKey)[] uninstallKeys =
        [
            (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall")
        ];

        foreach (var (root, subKey) in uninstallKeys)
        {
            try
            {
                using var parent = root.OpenSubKey(subKey);
                if (parent is null)
                {
                    continue;
                }

                foreach (var childName in parent.GetSubKeyNames())
                {
                    using var child = parent.OpenSubKey(childName);
                    var displayName = child?.GetValue("DisplayName") as string;
                    if (displayName is null ||
                        !displayName.Contains("SteelSeries GG", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (child!.GetValue("InstallLocation") is string location &&
                        !string.IsNullOrWhiteSpace(location) &&
                        Directory.Exists(location))
                    {
                        return location;
                    }
                }
            }
            catch
            {
                // Ignore and continue with other roots.
            }
        }

        return null;
    }

    private static IEnumerable<string> ProgramFilesRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var variable in new[] { "ProgramW6432", "ProgramFiles", "ProgramFiles(x86)" })
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
            {
                yield return value;
            }
        }
    }

    /// <summary>
    /// Splits a Run-key command into an executable path and its arguments, honouring quotes.
    /// </summary>
    private static (string? Path, string? Arguments) SplitCommandLine(string command)
    {
        command = command.Trim();
        if (command.Length == 0)
        {
            return (null, null);
        }

        if (command[0] == '"')
        {
            var end = command.IndexOf('"', 1);
            if (end < 0)
            {
                return (command.Trim('"'), null);
            }

            var path = command[1..end];
            var arguments = command[(end + 1)..].Trim();
            return (path, arguments.Length == 0 ? null : arguments);
        }

        // Unquoted: the path ends at the first ".exe".
        var exeIndex = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex >= 0)
        {
            var splitAt = exeIndex + 4;
            var path = command[..splitAt];
            var arguments = command[splitAt..].Trim();
            return (path, arguments.Length == 0 ? null : arguments);
        }

        var firstSpace = command.IndexOf(' ');
        return firstSpace < 0
            ? (command, null)
            : (command[..firstSpace], command[(firstSpace + 1)..].Trim());
    }
}
