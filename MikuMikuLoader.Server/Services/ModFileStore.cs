using System.Security.Cryptography;

namespace MikuMikuLoader.Server.Services;

public sealed class ModFileStore
{
    private readonly string _filesDir;

    public ModFileStore(IConfiguration cfg)
    {
        var dataDir = Path.GetFullPath(cfg["Server:DataDirectory"] ?? "data");
        _filesDir = Path.Combine(dataDir, "files");
        Directory.CreateDirectory(_filesDir);
    }

    public string DirFor(int modId) => Path.Combine(_filesDir, modId.ToString());
    public string PathFor(int modId, string fileName) => Path.Combine(DirFor(modId), fileName);

    public async Task<(long Size, string Sha256)> SaveAsync(int modId, string fileName, IFormFile file)
    {
        var dir = DirFor(modId);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);

        await using (var dest = File.Create(path))
        await using (var src = file.OpenReadStream())
        {
            await src.CopyToAsync(dest);
        }

        string sha;
        await using (var read = File.OpenRead(path))
        {
            sha = Convert.ToHexString(await SHA256.HashDataAsync(read)).ToLowerInvariant();
        }

        return (new FileInfo(path).Length, sha);
    }

    public void DeleteMod(int modId)
    {
        var dir = DirFor(modId);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    // Preview images live alongside the mod file, prefixed so they're never confused
    // with the payload itself.
    public string ImagePathFor(int modId, string fileName) =>
        Path.Combine(DirFor(modId), "image_" + fileName);

    // Saves an additional image; each gets a unique name so a mod can hold several.
    public async Task<string> SaveImageAsync(int modId, IFormFile file)
    {
        var dir = DirFor(modId);
        Directory.CreateDirectory(dir);

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var name = $"shot_{Guid.NewGuid():N}{ext}";
        var path = ImagePathFor(modId, name);

        await using (var dest = File.Create(path))
        await using (var src = file.OpenReadStream())
        {
            await src.CopyToAsync(dest);
        }

        return name;
    }

    public void DeleteImage(int modId, string fileName)
    {
        try
        {
            var p = ImagePathFor(modId, fileName);
            if (File.Exists(p)) File.Delete(p);
        }
        catch {  }
    }

    public void DeleteImages(int modId)
    {
        var dir = DirFor(modId);
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "image_*"))
        {
            try { File.Delete(f); } catch {  }
        }
    }
}
