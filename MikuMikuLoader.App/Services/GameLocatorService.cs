using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MikuMikuLoader.App.Services;

public class GameLocatorService
{
    public const string GameExe = "Gorilla Tag.exe";
    private const string GorillaTagDir = "Gorilla Tag";

    public bool IsValidGameFolder(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, GameExe));

    public string PluginsFolder(string gameFolder) =>
        Path.Combine(gameFolder, "BepInEx", "plugins");

    public string? Detect()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            var steam = GetSteamPath();
            if (steam is null) return null;

            foreach (var library in GetSteamLibraries(steam))
            {
                var candidate = Path.Combine(library, "steamapps", "common", GorillaTagDir);
                if (IsValidGameFolder(candidate))
                    return candidate;
            }
        }
        catch
        {

        }
        return null;
    }

    [SupportedOSPlatform("windows")]
    private static string? GetSteamPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return key?.GetValue("SteamPath") as string;
    }

    private static IEnumerable<string> GetSteamLibraries(string steamPath)
    {
        var libraries = new List<string> { steamPath };

        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            var text = File.ReadAllText(vdf);
            foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
                libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }

        return libraries.Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
