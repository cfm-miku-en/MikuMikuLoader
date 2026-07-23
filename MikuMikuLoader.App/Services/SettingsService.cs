using System.Text.Json;

namespace MikuMikuLoader.App.Services;

public class AppSettings
{

    public string ServerUrl { get; set; } = "https://loader.mikuuu.xyz";

    public string? GameFolder { get; set; }

    public string? AuthToken { get; set; }

    public string AcceptedTosVersion { get; set; } = "";

    public int LastSeenMotdId { get; set; }
}

public class SettingsService
{
    private readonly string _path;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MikuMikuLoader");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
        }
        catch
        {

        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, Json));
        }
        catch
        {

        }
    }
}
