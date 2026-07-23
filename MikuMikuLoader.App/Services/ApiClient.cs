using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MikuMikuLoader.App.Models;

namespace MikuMikuLoader.App.Services;

public class ApiException : Exception
{
    public ApiException(string message) : base(message) { }
}

public class ApiClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient _uploadHttp = new() { Timeout = TimeSpan.FromMinutes(30) };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string BaseUrl { get; private set; } = "";

    public void SetBaseUrl(string url)
    {
        var u = (url ?? "").Trim().TrimEnd('/');
        if (u.Length > 0 &&
            !u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var local = u.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
                        u.StartsWith("127.0.0.1");
            u = (local ? "http://" : "https://") + u;
        }
        BaseUrl = u;
    }

    public void SetToken(string? token)
    {
        var header = string.IsNullOrWhiteSpace(token) ? null : new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.Authorization = header;
        _uploadHttp.DefaultRequestHeaders.Authorization = header;
    }

    public async Task<ServerInfo?> GetInfoAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<ServerInfo>($"{BaseUrl}/api/info", Json, ct);

    public async Task<List<ModDto>> GetModsAsync(
        string? q, string? sort, bool trustedOnly, bool verifiedOnly, string? tag, string? kind = null, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(q)) parts.Add($"q={Uri.EscapeDataString(q)}");
        if (!string.IsNullOrWhiteSpace(sort)) parts.Add($"sort={Uri.EscapeDataString(sort)}");
        if (trustedOnly) parts.Add("trusted=true");
        if (verifiedOnly) parts.Add("verified=true");
        if (!string.IsNullOrWhiteSpace(tag)) parts.Add($"tag={Uri.EscapeDataString(tag)}");
        if (!string.IsNullOrWhiteSpace(kind)) parts.Add($"kind={Uri.EscapeDataString(kind)}");
        var query = parts.Count > 0 ? "?" + string.Join("&", parts) : "";
        return await _http.GetFromJsonAsync<List<ModDto>>($"{BaseUrl}/api/mods{query}", Json, ct) ?? new();
    }

    public async Task<List<TagDto>> GetTagsAsync(bool presetOnly, CancellationToken ct = default)
    {
        var query = presetOnly ? "?preset=true" : "";
        return await _http.GetFromJsonAsync<List<TagDto>>($"{BaseUrl}/api/tags{query}", Json, ct) ?? new();
    }

    public async Task<List<AnnouncementDto>> GetAnnouncementsAsync(string? kind = null, CancellationToken ct = default)
    {
        var query = string.IsNullOrWhiteSpace(kind) ? "" : $"?kind={Uri.EscapeDataString(kind)}";
        return await _http.GetFromJsonAsync<List<AnnouncementDto>>($"{BaseUrl}/api/announcements{query}", Json, ct) ?? new();
    }

    public async Task<AnnouncementDto?> GetMotdAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"{BaseUrl}/api/motd", ct);
        if (!resp.IsSuccessStatusCode) return null;
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "null") return null;
        return JsonSerializer.Deserialize<AnnouncementDto>(text, Json);
    }

    public async Task<List<CommentDto>> GetCommentsAsync(int modId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<CommentDto>>($"{BaseUrl}/api/mods/{modId}/comments", Json, ct) ?? new();

    public async Task<ReviewsResult> GetReviewsAsync(int modId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<ReviewsResult>($"{BaseUrl}/api/mods/{modId}/reviews", Json, ct) ?? new();

    public Task<AuthResponse> RegisterAsync(string username, string password, bool asDeveloper) =>
        PostJsonAsync<AuthResponse>("/api/auth/register", new { username, password, asDeveloper });

    public Task<AuthResponse> LoginAsync(string username, string password) =>
        PostJsonAsync<AuthResponse>("/api/auth/login", new { username, password });

    public async Task LogoutAsync()
    {
        try { await _http.PostAsync($"{BaseUrl}/api/auth/logout", null); } catch {  }
    }

    public async Task<Account?> GetMeAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"{BaseUrl}/api/auth/me", ct);
        if (resp.StatusCode == HttpStatusCode.Unauthorized) return null;
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<Account>(Json, ct);
    }

    public Task<Account> BecomeDeveloperAsync() =>
        PostJsonAsync<Account>("/api/auth/become-developer", null);

    public Task<Account> ApplyTrustedAsync() =>
        PostJsonAsync<Account>("/api/dev/apply-trusted", null);

    public async Task<List<ModDto>> GetDevModsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<ModDto>>($"{BaseUrl}/api/dev/mods", Json, ct) ?? new();

    public async Task<ModDto> UploadModAsync(
        string filePath, string name, string version, string author,
        string description, string dependencies, string kind, string repoUrl, IEnumerable<string>? imagePaths)
    {
        using var content = new MultipartFormDataContent();
        await using var fs = File.OpenRead(filePath);
        var fileContent = new StreamContent(fs);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", Path.GetFileName(filePath));

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) content.Add(new StringContent(value), key);
        }
        Add("name", name);
        Add("version", version);
        Add("author", author);
        Add("description", description);
        Add("dependencies", dependencies);
        Add("kind", kind);
        Add("repoUrl", repoUrl);

        var imageStreams = new List<FileStream>();
        foreach (var p in imagePaths ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(p) || !File.Exists(p)) continue;
            var fs2 = File.OpenRead(p);
            imageStreams.Add(fs2);
            var img = new StreamContent(fs2);
            img.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(img, "images", Path.GetFileName(p));
        }

        try
        {
            var resp = await _uploadHttp.PostAsync($"{BaseUrl}/api/mods", content);
            return await ReadOrThrow<ModDto>(resp);
        }
        finally
        {
            foreach (var fs2 in imageStreams) fs2.Dispose();
        }
    }


    public async Task<ModDto> UploadVersionAsync(int id, string filePath, string version)
    {
        using var content = new MultipartFormDataContent();
        await using var fs = File.OpenRead(filePath);
        var fileContent = new StreamContent(fs);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", Path.GetFileName(filePath));
        if (!string.IsNullOrWhiteSpace(version)) content.Add(new StringContent(version), "version");

        var resp = await _uploadHttp.PostAsync($"{BaseUrl}/api/mods/{id}/versions", content);
        return await ReadOrThrow<ModDto>(resp);
    }

    public Task<ModDto> UpdateModAsync(int id, string? version, string? description, string[]? dependencies, string? repoUrl = null) =>
        PutJsonAsync<ModDto>($"/api/mods/{id}", new { version, description, dependencies, repoUrl });

    public async Task<List<int>> GetImageIdsAsync(int modId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<int>>($"{BaseUrl}/api/mods/{modId}/images", Json, ct) ?? new();

    public async Task<byte[]?> GetImageBytesAsync(int imageId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"{BaseUrl}/api/mods/images/{imageId}", ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task AddImagesAsync(int modId, IEnumerable<string> paths)
    {
        using var content = new MultipartFormDataContent();
        var streams = new List<FileStream>();
        try
        {
            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;
                var fs = File.OpenRead(path);
                streams.Add(fs);
                var part = new StreamContent(fs);
                part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                content.Add(part, "images", Path.GetFileName(path));
            }
            if (streams.Count == 0) throw new ApiException("No images selected.");

            var resp = await _uploadHttp.PostAsync($"{BaseUrl}/api/mods/{modId}/images", content);
            await ThrowIfFailed(resp);
        }
        finally
        {
            foreach (var fs in streams) fs.Dispose();
        }
    }

    public async Task DeleteImageAsync(int imageId)
    {
        var resp = await _http.DeleteAsync($"{BaseUrl}/api/mods/images/{imageId}");
        await ThrowIfFailed(resp);
    }

    public async Task DeleteModAsync(int id)
    {
        var resp = await _http.DeleteAsync($"{BaseUrl}/api/mods/{id}");
        await ThrowIfFailed(resp);
    }

    public Task<ModDto> SetTagsAsync(int id, string[] tags) =>
        PutJsonAsync<ModDto>($"/api/mods/{id}/tags", new { tags });

    public Task<CommentDto> PostCommentAsync(int modId, string body) =>
        PostJsonAsync<CommentDto>($"/api/mods/{modId}/comments", new { body });

    public Task<ReviewDto> PostReviewAsync(int modId, int stars, string? body) =>
        PostJsonAsync<ReviewDto>($"/api/mods/{modId}/reviews", new { stars, body });

    private async Task<T> PostJsonAsync<T>(string path, object? body)
    {
        var resp = await _http.PostAsJsonAsync($"{BaseUrl}{path}", body ?? new { }, Json);
        return await ReadOrThrow<T>(resp);
    }

    private async Task<T> PutJsonAsync<T>(string path, object body)
    {
        var resp = await _http.PutAsJsonAsync($"{BaseUrl}{path}", body, Json);
        return await ReadOrThrow<T>(resp);
    }

    private static async Task<T> ReadOrThrow<T>(HttpResponseMessage resp)
    {
        await ThrowIfFailed(resp);
        var value = await resp.Content.ReadFromJsonAsync<T>(Json);
        return value ?? throw new ApiException("The server returned an empty response.");
    }

    private static async Task ThrowIfFailed(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode) return;
        var text = "";
        try { text = await resp.Content.ReadAsStringAsync(); } catch {  }
        if (string.IsNullOrWhiteSpace(text))
            text = $"Request failed ({(int)resp.StatusCode}).";
        throw new ApiException(text.Trim('"'));
    }
}
