using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using MikuMikuLoader.App.Models;

namespace MikuMikuLoader.App.Services;

public class InstallService
{
    private readonly ApiClient _api;
    private readonly GameLocatorService _locator;
    private readonly InstalledModsService _installed;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public InstallService(
        ApiClient api,
        GameLocatorService locator,
        InstalledModsService installed,
        SettingsService settingsService,
        AppSettings settings)
    {
        _api = api;
        _locator = locator;
        _installed = installed;
        _settingsService = settingsService;
        _settings = settings;
        GameFolder = ResolveGameFolder();
    }

    public string? GameFolder { get; private set; }

    public IReadOnlyCollection<InstalledMod> Installed => _installed.All;

    public bool IsInstalled(int modId) => _installed.IsInstalled(modId);
    public InstalledMod? GetInstalled(int modId) => _installed.Get(modId);

    private string? ResolveGameFolder()
    {
        if (_locator.IsValidGameFolder(_settings.GameFolder))
            return _settings.GameFolder;
        return _locator.Detect();
    }

    public string? Detect()
    {
        var found = _locator.Detect();
        if (found is not null)
            SetGameFolder(found);
        return found;
    }

    public void SetGameFolder(string folder)
    {
        _settings.GameFolder = folder;
        _settingsService.Save(_settings);
        GameFolder = folder;
    }

    public async Task InstallAsync(ModDto mod)
    {
        if (GameFolder is null)
            throw new InvalidOperationException("Gorilla Tag folder isn't set.");

        if (string.Equals(mod.Kind, "Reshade", StringComparison.OrdinalIgnoreCase))
            await InstallReshadeAsync(mod, GameFolder);
        else
            await InstallModAsync(mod, GameFolder);
    }

    private async Task InstallModAsync(ModDto mod, string gameFolder)
    {
        var plugins = _locator.PluginsFolder(gameFolder);
        Directory.CreateDirectory(plugins);

        var url = $"{_api.BaseUrl}/api/mods/{mod.Id}/download";
        var fileName = SanitizeFileName(mod.FileName, mod.Id);
        var dest = Path.Combine(plugins, fileName);

        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        await using (var fs = File.Create(dest))
        await using (var stream = await resp.Content.ReadAsStreamAsync())
        {
            await stream.CopyToAsync(fs);
        }

        _installed.Upsert(new InstalledMod
        {
            ModId = mod.Id,
            Name = mod.Name,
            Version = mod.Version,
            FileName = fileName,
            Kind = "Mod",
            InstalledAt = DateTimeOffset.Now
        });
    }

    private async Task InstallReshadeAsync(ModDto mod, string gameFolder)
    {
        var url = $"{_api.BaseUrl}/api/mods/{mod.Id}/download";
        var isIni = mod.FileName.EndsWith(".ini", StringComparison.OrdinalIgnoreCase);
        var written = new List<string>();

        if (isIni)
        {
            // Bare preset: drop the .ini straight into the game root.
            var name = SanitizeFileName(mod.FileName, mod.Id);
            var dest = Path.Combine(gameFolder, name);
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            await using (var fs = File.Create(dest))
            await using (var stream = await resp.Content.ReadAsStreamAsync())
            {
                await stream.CopyToAsync(fs);
            }
            written.Add(name);
        }
        else
        {
            var tempZip = Path.Combine(Path.GetTempPath(), $"mml-reshade-{mod.Id}.zip");

            using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(tempZip);
                await using var stream = await resp.Content.ReadAsStreamAsync();
                await stream.CopyToAsync(fs);
            }

            using (var zip = ZipFile.OpenRead(tempZip))
            {
                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // directory
                    var rel = MapReshadeEntry(entry.FullName);
                    if (rel is null) continue;

                    var dest = Path.Combine(gameFolder, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    entry.ExtractToFile(dest, overwrite: true);
                    written.Add(rel);
                }
            }

            try { File.Delete(tempZip); } catch {  }
        }

        _installed.Upsert(new InstalledMod
        {
            ModId = mod.Id,
            Name = mod.Name,
            Version = mod.Version,
            FileName = "",
            Kind = "Reshade",
            Files = written,
            InstalledAt = DateTimeOffset.Now
        });
    }

    // Maps a zip entry to a path relative to the Gorilla Tag root:
    //  - top-level .ini  -> root
    //  - Shaders/...      -> reshade-shaders/Shaders/...
    //  - Textures/...     -> reshade-shaders/Textures/...
    //  - reshade-shaders/ -> reshade-shaders/... (as-is)
    private static string? MapReshadeEntry(string fullName)
    {
        var p = fullName.Replace('\\', '/').TrimStart('/');
        if (p.Length == 0) return null;
        var lower = p.ToLowerInvariant();

        if (lower.StartsWith("shaders/"))
            return Path.Combine("reshade-shaders", "Shaders", p.Substring("shaders/".Length));
        if (lower.StartsWith("textures/"))
            return Path.Combine("reshade-shaders", "Textures", p.Substring("textures/".Length));
        if (lower.StartsWith("reshade-shaders/"))
            return p.Replace('/', Path.DirectorySeparatorChar);
        if (!p.Contains('/'))
            return Path.GetFileName(p); // top-level file (ini, readme, etc.) -> root

        // nested under some other folder: keep the structure under root
        return p.Replace('/', Path.DirectorySeparatorChar);
    }

    public IReadOnlyList<InstalledMod> InstalledReshades =>
        _installed.All.Where(m => string.Equals(m.Kind, "Reshade", StringComparison.OrdinalIgnoreCase)).ToList();

    public void UninstallReshade(InstalledMod entry)
    {
        if (GameFolder is not null && entry.Files is not null)
        {
            foreach (var rel in entry.Files)
            {
                try
                {
                    var f = Path.Combine(GameFolder, rel);
                    if (File.Exists(f)) File.Delete(f);
                }
                catch {  }
            }
        }
        _installed.Remove(entry.ModId);
    }

    public void Uninstall(int modId)
    {
        var entry = _installed.Get(modId);
        if (entry is not null && GameFolder is not null)
        {
            try
            {
                var path = Path.Combine(_locator.PluginsFolder(GameFolder), entry.FileName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {

            }
        }
        _installed.Remove(modId);
    }

    public void OpenPluginsFolder()
    {
        if (GameFolder is null) return;
        try
        {
            var plugins = _locator.PluginsFolder(GameFolder);
            Directory.CreateDirectory(plugins);
            Process.Start(new ProcessStartInfo(plugins) { UseShellExecute = true });
        }
        catch
        {

        }
    }

    public void LaunchGame()
    {
        try
        {
            Process.Start(new ProcessStartInfo("steam://rungameid/1533390") { UseShellExecute = true });
        }
        catch
        {

        }
    }

    public IReadOnlyList<PluginFile> ScanPlugins()
    {
        var result = new List<PluginFile>();
        if (GameFolder is null) return result;

        var dir = _locator.PluginsFolder(GameFolder);
        if (!Directory.Exists(dir)) return result;

        var tracked = _installed.All;

        foreach (var path in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
        {
            var file = Path.GetFileName(path);
            var enabled = file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            var disabled = file.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase);
            if (!enabled && !disabled) continue;

            var baseName = enabled ? file : file[..^".disabled".Length];
            var match = tracked.FirstOrDefault(m => string.Equals(m.FileName, baseName, StringComparison.OrdinalIgnoreCase));

            result.Add(new PluginFile
            {
                Path = path,
                FileName = baseName,
                Enabled = enabled,
                IsTracked = match is not null,
                ModId = match?.ModId ?? 0,
                Name = match?.Name ?? Path.GetFileNameWithoutExtension(baseName),
                Version = match?.Version ?? ""
            });
        }

        return result.OrderBy(p => p.Name).ToList();
    }

    public void SetPluginEnabled(PluginFile plugin, bool enabled)
    {
        try
        {
            if (enabled && plugin.Path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                File.Move(plugin.Path, plugin.Path[..^".disabled".Length]);
            else if (!enabled && plugin.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                File.Move(plugin.Path, plugin.Path + ".disabled");
        }
        catch
        {

        }
    }

    public void DeletePlugin(PluginFile plugin)
    {
        try
        {
            if (File.Exists(plugin.Path)) File.Delete(plugin.Path);
        }
        catch
        {

        }
        if (plugin.ModId > 0) _installed.Remove(plugin.ModId);
    }

    private static string SanitizeFileName(string raw, int modId)
    {
        var name = Path.GetFileName(raw ?? "");
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        if (string.IsNullOrWhiteSpace(name))
            name = $"mod-{modId}.dll";
        return name;
    }
}

public class PluginFile
{
    public string Path { get; set; } = "";
    public string FileName { get; set; } = "";
    public bool Enabled { get; set; }
    public bool IsTracked { get; set; }
    public int ModId { get; set; }
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
}
