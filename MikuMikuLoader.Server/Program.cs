using System.Text.Json;
using MikuMikuLoader.Server.Data;
using MikuMikuLoader.Server.Endpoints;
using MikuMikuLoader.Server.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var dataDir = Path.GetFullPath(builder.Configuration["Server:DataDirectory"] ?? "data");
Directory.CreateDirectory(dataDir);

var connectionString = $"Data Source={Path.Combine(dataDir, "gorillaloader.db")}";
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));

builder.Services.AddSingleton<ModFileStore>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

var maxUpload = builder.Configuration.GetValue<long?>("Server:MaxUploadBytes") ?? 50L * 1024 * 1024;
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = maxUpload + 1024 * 1024);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = maxUpload + 1024 * 1024;
    o.ValueLengthLimit = int.MaxValue;
});

var app = builder.Build();
app.UseCors();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = c =>
    {
        // The console is a single HTML file that changes with every server update;
        // never let a browser serve a stale copy.
        if (c.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            c.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            c.Context.Response.Headers["Pragma"] = "no-cache";
            c.Context.Response.Headers["Expires"] = "0";
        }
    }
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // Lightweight in-place migration for databases created before these columns
    // existed. ALTER throws if the column is already there, which we ignore.
    async Task AddColumn(string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); } catch { }
    }
    await AddColumn("ALTER TABLE Mods ADD COLUMN Kind INTEGER NOT NULL DEFAULT 0;");
    await AddColumn("ALTER TABLE Mods ADD COLUMN FeaturedUntil TEXT NULL;");
    await AddColumn("ALTER TABLE Mods ADD COLUMN Trusted INTEGER NOT NULL DEFAULT 0;");
    await AddColumn("ALTER TABLE Mods ADD COLUMN Locked INTEGER NOT NULL DEFAULT 0;");
    await AddColumn("ALTER TABLE Mods ADD COLUMN Verified INTEGER NOT NULL DEFAULT 0;");
    await AddColumn("ALTER TABLE Mods ADD COLUMN RepoUrl TEXT NOT NULL DEFAULT '';");
    await AddColumn("ALTER TABLE Mods ADD COLUMN ImageFileName TEXT NOT NULL DEFAULT '';");

    // Trust used to be inherited from the developer's account. It's now per-mod, so
    // without this the badge would silently vanish from every existing mod. Runs once,
    // tracked in ServerMeta, so re-running the server never re-trusts something you untrusted.
    await db.Database.ExecuteSqlRawAsync(
        "CREATE TABLE IF NOT EXISTS ServerMeta (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);");

    // EnsureCreated only builds tables for a brand-new database, so an existing
    // deployment needs this table created explicitly.
    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ModImages (
            Id INTEGER NOT NULL CONSTRAINT PK_ModImages PRIMARY KEY AUTOINCREMENT,
            ModId INTEGER NOT NULL,
            FileName TEXT NOT NULL,
            Sort INTEGER NOT NULL DEFAULT 0,
            CreatedAt TEXT NOT NULL,
            CONSTRAINT FK_ModImages_Mods_ModId FOREIGN KEY (ModId) REFERENCES Mods (Id) ON DELETE CASCADE
        );");

    // Move any single legacy image into the new gallery table.
    await db.Database.ExecuteSqlRawAsync(@"
        INSERT INTO ModImages (ModId, FileName, Sort, CreatedAt)
        SELECT Id, ImageFileName, 0, CreatedAt FROM Mods
        WHERE ImageFileName <> '' AND Id NOT IN (SELECT ModId FROM ModImages);");

    var backfilled = await db.Database
        .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ServerMeta WHERE Key = 'trusted_backfill'")
        .FirstAsync();

    if (backfilled == 0)
    {
        var updated = await db.Database.ExecuteSqlRawAsync(
            "UPDATE Mods SET Trusted = 1 WHERE Trusted = 0 AND OwnerId IN (SELECT Id FROM Accounts WHERE TrustStatus = 1);");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO ServerMeta (Key, Value) VALUES ('trusted_backfill', '1');");
        if (updated > 0)
            app.Logger.LogInformation("Per-mod trust backfill: {Count} existing mod(s) kept their trusted badge.", updated);
    }
    await AddColumn("ALTER TABLE Comments ADD COLUMN Hidden INTEGER NOT NULL DEFAULT 0;");
    await AddColumn("ALTER TABLE Comments ADD COLUMN Reports INTEGER NOT NULL DEFAULT 0;");
    await AddColumn("ALTER TABLE Accounts ADD COLUMN CanModerate INTEGER NOT NULL DEFAULT 0;");
    await AddColumn("ALTER TABLE Accounts ADD COLUMN CanManageMods INTEGER NOT NULL DEFAULT 0;");
    await AddColumn("ALTER TABLE Accounts ADD COLUMN CanVerify INTEGER NOT NULL DEFAULT 0;");
    await AddColumn("ALTER TABLE Accounts ADD COLUMN CanPostNews INTEGER NOT NULL DEFAULT 0;");

    await OwnerBootstrap.EnsureOwnerAsync(db, builder.Configuration);
}

app.MapPublicEndpoints();
app.MapAuthEndpoints();
app.MapDevEndpoints();
app.MapOwnerEndpoints();

app.MapGet("/admin", () => Results.Redirect("/admin.html"));

app.Run();
