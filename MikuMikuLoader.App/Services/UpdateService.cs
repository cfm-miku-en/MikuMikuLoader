using System.Net.Http;
using System.Text.Json;

namespace MikuMikuLoader.App.Services;

public static class AppInfo
{

    public const string Version = "0.3.0";

    public const string ReleasesPage = "https://github.com/cfm-miku-en/MikuMikuLoader/releases";
    private const string LatestReleaseApi = "https://api.github.com/repos/cfm-miku-en/MikuMikuLoader/releases/latest";

    public static string LatestApi => LatestReleaseApi;
}

public record UpdateInfo(string Version, string Url);

public class UpdateService
{
    private readonly HttpClient _http;

    public UpdateService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MikuMikuLoader");
    }

    public async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(AppInfo.LatestApi);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var url = root.TryGetProperty("html_url", out var h) ? h.GetString() : null;

            var latest = tag.TrimStart('v', 'V');
            if (IsNewer(latest, AppInfo.Version))
                return new UpdateInfo(latest, url ?? AppInfo.ReleasesPage);

            return null;
        }
        catch
        {

            return null;
        }
    }

    private static bool IsNewer(string latest, string current)
    {
        if (Version.TryParse(latest, out var l) && Version.TryParse(current, out var c))
            return l > c;
        return !string.IsNullOrWhiteSpace(latest) &&
               !string.Equals(latest, current, StringComparison.OrdinalIgnoreCase);
    }
}
