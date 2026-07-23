using MikuMikuLoader.Server.Data;
using MikuMikuLoader.Server.Dtos;
using MikuMikuLoader.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace MikuMikuLoader.Server.Endpoints;

public static class PublicEndpoints
{
    public static void MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", (IConfiguration cfg) =>
            Results.Text($"{cfg["Server:Name"] ?? "MikuMikuLoader"} — mod server. Try /api/info", "text/plain"));

        app.MapGet("/api/info", async (AppDbContext db, IConfiguration cfg) =>
        {
            var modCount = await db.Mods.CountAsync(m => m.Status == ModStatus.Published);
            var allowUploads = !bool.TryParse(cfg["Server:AllowUploads"], out var au) || au;
            return Results.Ok(new ServerInfo(
                cfg["Server:Name"] ?? "MikuMikuLoader",
                cfg["Server:Description"] ?? "",
                "MikuMikuLoader.Server", "0.2.0", 2, modCount, allowUploads));
        });

        app.MapGet("/api/mods", async (AppDbContext db, string? q, string? sort, bool? trusted, bool? verified, string? tag, string? kind) =>
        {
            var query = db.Mods.Where(m => m.Status == ModStatus.Published && !m.Owner.IsBanned);

            if (trusted == true)
                query = query.Where(m => m.Trusted);

            if (verified == true)
                query = query.Where(m => m.Verified);

            if (Enum.TryParse<ModKind>(kind, ignoreCase: true, out var k))
                query = query.Where(m => m.Kind == k);

            if (!string.IsNullOrWhiteSpace(tag))
            {
                var tg = tag.Trim().ToLowerInvariant();
                query = query.Where(m => m.Tags.Any(t => t.Name == tg));
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var n = q.Trim();
                query = query.Where(m =>
                    EF.Functions.Like(m.Name, $"%{n}%") ||
                    EF.Functions.Like(m.Author, $"%{n}%") ||
                    EF.Functions.Like(m.Description, $"%{n}%"));
            }

            query = sort switch
            {
                "downloads" => query.OrderByDescending(m => m.Downloads),
                "name" => query.OrderBy(m => m.Name),
                "rating" => query.OrderByDescending(m =>
                    m.Reviews.Count == 0 ? 0.0 : m.Reviews.Average(r => (double)r.Stars)),
                _ => query.OrderByDescending(m => m.Id)
            };

            var rows = await query.Select(Projections.RowExpr).ToListAsync();

            // Featured items float to the top when browsing (not when searching).
            // Done in memory because SQLite can't ORDER BY a DateTimeOffset.
            if (string.IsNullOrWhiteSpace(q))
            {
                var now = DateTimeOffset.UtcNow;
                rows = rows.OrderByDescending(r => r.FeaturedUntil.HasValue && r.FeaturedUntil.Value > now).ToList();
            }

            return Results.Ok(rows.Select(Projections.ToDto));
        });

        app.MapGet("/api/mods/{id:int}", async (AppDbContext db, int id) =>
        {
            var row = await db.Mods
                .Where(m => m.Id == id && m.Status == ModStatus.Published)
                .Select(Projections.RowExpr)
                .FirstOrDefaultAsync();
            return row is null ? Results.NotFound() : Results.Ok(Projections.ToDto(row));
        });

        app.MapGet("/api/mods/{id:int}/download", async (AppDbContext db, ModFileStore files, int id) =>
        {
            var mod = await db.Mods.FirstOrDefaultAsync(m => m.Id == id);
            if (mod is null || mod.Status != ModStatus.Published) return Results.NotFound();

            var path = files.PathFor(mod.Id, mod.FileName);
            if (!File.Exists(path)) return Results.NotFound();

            mod.Downloads++;
            await db.SaveChangesAsync();

            return Results.File(File.OpenRead(path), "application/octet-stream", mod.FileName);
        });

        // Image ids for a mod, in display order.
        app.MapGet("/api/mods/{id:int}/images", async (AppDbContext db, int id) =>
        {
            var ids = await db.ModImages
                .Where(i => i.ModId == id)
                .OrderBy(i => i.Sort).ThenBy(i => i.Id)
                .Select(i => i.Id)
                .ToListAsync();
            return Results.Ok(ids);
        });

        // Serve one image by its id.
        app.MapGet("/api/mods/images/{imageId:int}", async (AppDbContext db, ModFileStore files, int imageId) =>
        {
            var image = await db.ModImages.FirstOrDefaultAsync(i => i.Id == imageId);
            if (image is null) return Results.NotFound();

            var path = files.ImagePathFor(image.ModId, image.FileName);
            if (!File.Exists(path)) return Results.NotFound();

            var ext = Path.GetExtension(image.FileName).ToLowerInvariant();
            var mime = ext switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
            return Results.File(path, mime);
        });

        app.MapGet("/api/mods/{id:int}/comments", async (AppDbContext db, int id) =>
        {
            var comments = await db.Comments
                .Where(c => c.ModId == id && !c.Hidden && !c.Account.IsBanned)
                .OrderByDescending(c => c.Id)
                .Select(c => new CommentDto(c.Id, c.ModId, c.Account.Username, c.Body, c.CreatedAt))
                .ToListAsync();
            return Results.Ok(comments);
        });

        app.MapGet("/api/mods/{id:int}/reviews", async (AppDbContext db, int id) =>
        {
            var reviews = await db.Reviews
                .Where(r => r.ModId == id)
                .OrderByDescending(r => r.Id)
                .Select(r => new ReviewDto(r.Id, r.ModId, r.Account.Username, r.Stars, r.Body, r.CreatedAt))
                .ToListAsync();

            var rating = reviews.Count == 0 ? 0.0 : Math.Round(reviews.Average(r => r.Stars), 2);
            return Results.Ok(new { rating, count = reviews.Count, reviews });
        });

        app.MapGet("/api/tags", async (AppDbContext db, bool? preset) =>
        {
            var query = db.Tags.AsQueryable();
            if (preset == true) query = query.Where(t => t.IsPreset);

            var tags = await query
                .OrderByDescending(t => t.IsPreset)
                .ThenBy(t => t.Name)
                .Select(t => new TagDto(t.Id, t.Name, t.IsPreset, t.Mods.Count))
                .ToListAsync();
            return Results.Ok(tags);
        });

        app.MapGet("/api/announcements", async (AppDbContext db, string? kind) =>
        {
            var query = db.Announcements.Where(a => a.Published);
            if (Enum.TryParse<AnnouncementKind>(kind, ignoreCase: true, out var k))
                query = query.Where(a => a.Kind == k);

            var list = await query.OrderByDescending(a => a.Id).ToListAsync();
            return Results.Ok(list.Select(Mapper.Announcement));
        });

        app.MapGet("/api/motd", async (AppDbContext db) =>
        {
            var motd = await db.Announcements
                .Where(a => a.Published && a.Kind == AnnouncementKind.Motd)
                .OrderByDescending(a => a.Id)
                .FirstOrDefaultAsync();
            return Results.Ok(motd is null ? null : Mapper.Announcement(motd));
        });
    }
}
