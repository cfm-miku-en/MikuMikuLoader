using System.Text.Json;

namespace MikuMikuLoader.App.Services;

public class InstalledMod
{
    public int ModId { get; set; }
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Kind { get; set; } = "Mod";
    public List<string>? Files { get; set; }
    public DateTimeOffset InstalledAt { get; set; }
}

public class InstalledModsService
{
    private readonly string _path;
    private Dictionary<int, InstalledMod> _map = new();

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public InstalledModsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MikuMikuLoader");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "installed.json");
        Load();
    }

    public IReadOnlyCollection<InstalledMod> All =>
        _map.Values.OrderBy(m => m.Name).ToList();

    public bool IsInstalled(int modId) => _map.ContainsKey(modId);

    public InstalledMod? Get(int modId) => _map.TryGetValue(modId, out var m) ? m : null;

    public void Upsert(InstalledMod mod)
    {
        _map[mod.ModId] = mod;
        Save();
    }

    public void Remove(int modId)
    {
        if (_map.Remove(modId))
            Save();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var list = JsonSerializer.Deserialize<List<InstalledMod>>(File.ReadAllText(_path));
                if (list is not null)
                    _map = list.ToDictionary(m => m.ModId);
            }
        }
        catch
        {
            _map = new Dictionary<int, InstalledMod>();
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_map.Values.ToList(), Json));
        }
        catch
        {

        }
    }
}
