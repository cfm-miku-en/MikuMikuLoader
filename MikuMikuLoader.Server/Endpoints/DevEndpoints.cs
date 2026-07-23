using MikuMikuLoader.Server.Data;
using MikuMikuLoader.Server.Dtos;
using MikuMikuLoader.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace MikuMikuLoader.Server.Endpoints;

public static class DevEndpoints
{
    // Only accept plain http(s) links, so a repo field can't smuggle javascript: or data: URLs.
    private static string SafeRepoUrl(string? raw)
    {
        var v = (raw ?? "").Trim();
        if (v.Length == 0) return "";
        if (v.Length > 300) return "";
        return Uri.TryCreate(v, UriKind.Absolute, out var u) &&
               (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
            ? v : "";
    }

    public static void MapDevEndpoints(this IEndpointRouteBuilder app)
    {

        app.MapPost("/api/mods", async (HttpContext ctx, AppDbContext db, ModFileStore files, IConfiguration cfg) =>
        {
            var account = await ApiAuth.CurrentAsync(ctx, db);
            if (account is null) return Results.Unauthorized();
            if (account.Role == AccountRole.User) return Results.StatusCode(403);

            var allowUploads = !bool.TryParse(cfg["Server:AllowUploads"], out var au) || au;
            if (!allowUploads) return Results.StatusCode(403);

            if (!ctx.Request.HasFormContentType) return Results.BadRequest("Expected multipart/form-data.");
            var form = await ctx.Request.ReadFormAsync();

            var file = form.Files["file"];
            if (file is null || file.Length == 0) return Results.BadRequest("Missing 'file' upload.");

            var maxBytes = long.TryParse(cfg["Server:MaxUploadBytes"], out var mb) ? mb : 50L * 1024 * 1024;
            if (file.Length > maxBytes) return Results.BadRequest($"File exceeds the {maxBytes}-byte limit.");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var kind = string.Equals(Util.Nullify(form["kind"]), "reshade", StringComparison.OrdinalIgnoreCase)
                ? ModKind.Reshade
                : ModKind.Mod;

            if (kind == ModKind.Reshade)
            {
                if (ext != ".zip" && ext != ".ini") return Results.BadRequest("Reshades must be a .zip (preset plus Shaders/Textures) or a bare .ini preset.");
            }
            else if (ext != ".dll" && ext != ".zip")
            {
                return Results.BadRequest("Only .dll or .zip mods are allowed.");
            }

            var mod = new Mod
            {
                OwnerId = account.Id,
                Name = Util.Nullify(form["name"]) ?? Path.GetFileNameWithoutExtension(file.FileName),
                Version = Util.Nullify(form["version"]) ?? "1.0.0",
                Author = Util.Nullify(form["author"]) ?? account.Username,
                Description = Util.Nullify(form["description"]) ?? "",
                GtVersion = "",
                DependenciesCsv = Util.Nullify(form["dependencies"]) ?? "",
                FileName = Path.GetFileName(file.FileName),
                RepoUrl = SafeRepoUrl(form["repoUrl"]),
                Kind = kind,
                Status = ModStatus.Published,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Mods.Add(mod);
            await db.SaveChangesAsync();

            try
            {
                var (size, sha) = await files.SaveAsync(mod.Id, mod.FileName, file);
                mod.FileSizeBytes = size;
                mod.Sha256 = sha;

                var sort = 0;
                foreach (var image in form.Files.Where(f => f.Name == "image" || f.Name == "images"))
                {
                    if (image.Length <= 0) continue;
                    var imgExt = Path.GetExtension(image.FileName).ToLowerInvariant();
                    if (imgExt is not (".png" or ".jpg" or ".jpeg" or ".webp")) continue;
                    if (image.Length > 8 * 1024 * 1024) continue;
                    if (sort >= 8) break;

                    var stored = await files.SaveImageAsync(mod.Id, image);
                    db.ModImages.Add(new ModImage
                    {
                        ModId = mod.Id,
                        FileName = stored,
                        Sort = sort++,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                }

                await db.SaveChangesAsync();
            }
            catch
            {
                db.Mods.Remove(mod);
                await db.SaveChangesAsync();
                files.DeleteMod(mod.Id);
                return Results.Problem("Failed to store the uploaded file.");
            }

            var row = await db.Mods.Where(m => m.Id == mod.Id).Select(Projections.RowExpr).FirstAsync();
            return Results.Created($"/api/mods/{mod.Id}", Projections.ToDto(row));
        });

        app.MapPost("/api/mods/{id:int}/versions", async (HttpContext ctx, AppDbContext db, ModFileStore files, IConfiguration cfg, int id) =>
        {
            var account = await ApiAuth.CurrentAsync(ctx, db);
            if (account is null) return Results.Unauthorized();
            if (account.Role == AccountRole.User) return Results.StatusCode(403);

            var mod = await db.Mods.FirstOrDefaultAsync(m => m.Id == id);
            if (mod is null) return Results.NotFound();
            if (mod.OwnerId != account.Id && account.Role != AccountRole.Owner) return Results.StatusCode(403);
            if (mod.Locked && account.Role != AccountRole.Owner) return Results.StatusCode(403);

            if (!ctx.Request.HasFormContentType) return Results.BadRequest("Expected multipart/form-data.");
            var form = await ctx.Request.ReadFormAsync();

            var file = form.Files["file"];
            if (file is null || file.Length == 0) return Results.BadRequest("Missing 'file' upload.");

            var maxBytes = long.TryParse(cfg["Server:MaxUploadBytes"], out var mb) ? mb : 50L * 1024 * 1024;
            if (file.Length > maxBytes) return Results.BadRequest($"File exceeds the {maxBytes}-byte limit.");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".dll" && ext != ".zip") return Results.BadRequest("Only .dll or .zip mods are allowed.");

            var newVersion = Util.Nullify(form["version"]) ?? mod.Version;

            files.DeleteMod(mod.Id);
            var (size, sha) = await files.SaveAsync(mod.Id, Path.GetFileName(file.FileName), file);

            mod.Version = newVersion;
            mod.FileName = Path.GetFileName(file.FileName);
            mod.FileSizeBytes = size;
            mod.Sha256 = sha;
            mod.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            var row = await db.Mods.Where(m => m.Id == mod.Id).Select(Projections.RowExpr).FirstAsync();
            return Results.Ok(Projections.ToDto(row));
        });

        // Add images to an existing mod (max 8).
        app.MapPost("/api/mods/{id:int}/images", async (HttpContext ctx, AppDbContext db, ModFileStore files, int id) =>
        {
            var account = await ApiAuth.CurrentAsync(ctx, db);
            if (account is null) return Results.Unauthorized();
            if (account.Role == AccountRole.User) return Results.StatusCode(403);

            var mod = await db.Mods.FirstOrDefaultAsync(m => m.Id == id);
            if (mod is null) return Results.NotFound();
            if (mod.OwnerId != account.Id && account.Role != AccountRole.Owner) return Results.StatusCode(403);
            if (mod.Locked && account.Role != AccountRole.Owner) return Results.StatusCode(403);

            if (!ctx.Request.HasFormContentType) return Results.BadRequest("Expected multipart/form-data.");
            var form = await ctx.Request.ReadFormAsync();

            var existing = await db.ModImages.CountAsync(i => i.ModId == id);
            var sort = existing;
            var added = 0;

            foreach (var image in form.Files.Where(f => f.Name == "image" || f.Name == "images"))
            {
                if (image.Length <= 0) continue;
                var ext2 = Path.GetExtension(image.FileName).ToLowerInvariant();
                if (ext2 is not (".png" or ".jpg" or ".jpeg" or ".webp"))
                    return Results.BadRequest("Images must be png, jpg or webp.");
                if (image.Length > 8 * 1024 * 1024)
                    return Results.BadRequest("Each image must be 8 MB or smaller.");
                if (sort >= 8) break;

                var stored = await files.SaveImageAsync(id, image);
                db.ModImages.Add(new ModImage
                {
                    ModId = id,
                    FileName = stored,
                    Sort = sort++,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                added++;
            }

            if (added == 0) return Results.BadRequest("No usable images were provided.");
            await db.SaveChangesAsync();
            return Results.Ok(new { added, total = sort });
        });

        app.MapDelete("/api/mods/images/{imageId:int}", async (HttpContext ctx, AppDbContext db, ModFileStore files, int imageId) =>
        {
            var account = await ApiAuth.CurrentAsync(ctx, db);
            if (account is null) return Results.Unauthorized();

            var image = await db.ModImages.Include(i => i.Mod).FirstOrDefaultAsync(i => i.Id == imageId);
            if (image is null) return Results.NotFound();
            if (image.Mod.OwnerId != account.Id && account.Role != AccountRole.Owner) return Results.StatusCode(403);
            if (image.Mod.Locked && account.Role != AccountRole.Owner) return Results.StatusCode(403);

            files.DeleteImage(image.ModId, image.FileName);
            db.ModImages.Remove(image);
            await db.SaveChangesAsync();
            return Results.Ok(new { deleted = imageId });
        });

        app.MapPut("/api/mods/{id:int}", async (HttpContext ctx, AppDbContext db, int id, ModEditRequest req) =>
        {
            var account = await ApiAuth.CurrentAsync(ctx, db);
            if (account is null) return Results.Unauthorized();
            if (account.Role == AccountRole.User) return Results.StatusCode(403);

            var mod = await db.Mods.FirstOrDefaultAsync(m => m.Id == id);
            if (mod is null) return Results.NotFound();
            if (mod.OwnerId != account.Id && account.Role != AccountRole.Owner) return Results.StatusCode(403);
            if (mod.Locked && account.Role != AccountRole.Owner) return Results.StatusCode(403);

            if (Util.Nullify(req.Version) is { } v) mod.Version = v;
            if (req.Description is not null) mod.Description = req.Description.Trim();
            if (req.Dependencies is not null) mod.DependenciesCsv = string.Join(",", req.Dependencies);
            if (req.RepoUrl is not null) mod.RepoUrl = SafeRepoUrl(req.RepoUrl);
            mod.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            var row = await db.Mods.Where(m => m.Id == id).Select(Projections.RowExpr).FirstAsync();
            return Results.Ok(Projections.ToDto(row));
        });

        app.MapDelete("/api/mods/{id:int}", async (HttpContext ctx, AppDbContext db, ModFileStore files, int id) =>
        {
            var account = await ApiAuth.CurrentAsync(ctx, db);
            if (account is null) return Results.Unauthorized();
            if (account.Role == AccountRole.User) return Results.StatusCode(403);

            var mod = await db.Mods.FirstOrDefaultAsync(m => m.Id == id);
            if (mod is null) return Results.NotFound();
            if (mod.OwnerId != account.Id && account.Role != AccountRole.Owner) return Results.StatusCode(403);
            if (mod.Locked && account.Role != AccountRole.Owner) return Results.StatusCode(403);

            db.Mods.Remove(mod);
            await db.SaveChangesAsync();
            files.DeleteMod(id);
            return Results.NoContent();
        });

        app.MapGet("/api/dev/mods", async (HttpContext ctx, AppDbContext db) =>
        {
            var account = await ApiAuth.CurrentAsync(ctx, db);
            if (account is null) return Results.Unauthorized();
            if (account.Role == AccountRole.User) return Results.StatusCode(403);

            var rows = await db.Mods
                .Where(m => m.OwnerId == account.Id)
                .OrderByDescending(m => m.Id)
                .Select(Projections.RowExpr)
                .ToListAsync();
            return Results.Ok(rows.Select(Projections.ToDto));
        });

        app.MapPost("/api/dev/apply-trusted", async (HttpContext ctx, AppDbContext db) =>
        {
            var account = await ApiAuth.CurrentAsync(ctx, db);
            if (account is null) return Results.Unauthorized();
            if (account.Role == AccountRole.User) return Results.StatusCode(403);

            if (account.TrustStatus == TrustStatus.Trusted) return Results.BadRequest("You're already trusted.");
            if (account.TrustStatus == TrustStatus.Pending) return Results.BadRequest("Your application is already pending.");

            account.TrustStatus = TrustStatus.Pending;
            await db.SaveChangesAsync();
            return Results.Ok(Mapper.Account(account));
        });

        app.MapPost("/api/mods/{id:int}/comments", async (HttpContext ctx, AppDbContext db, int id, CommentRequest req) =>
        {
            var account = await ApiAuth.CurrentAsync(ctx, db);
            if (account is null) return Results.Unauthorized();
            if (account.IsBanned) return Results.StatusCode(403);
            if (string.IsNullOrWhiteSpace(req.Body)) return Results.BadRequest("Comment body is required.");

            var body = req.Body.Trim();
            if (body.Length > 1000) return Results.BadRequest("Comments are limited to 1000 characters.");
            if (!await db.Mods.AnyAsync(m => m.Id == id)) return Results.NotFound();

            // Rate limit: max 5 comments per minute, and no duplicate of your last
            // comment on this mod (stops copy-paste spam). SQLite can't compare
            // DateTimeOffset in SQL, so the timestamps are checked in memory.
            var recentTimes = await db.Comments
                .Where(c => c.AccountId == account.Id)
                .OrderByDescending(c => c.Id)
                .Take(5)
                .Select(c => c.CreatedAt)
                .ToListAsync();

            var since = DateTimeOffset.UtcNow.AddMinutes(-1);
            if (recentTimes.Count >= 5 && recentTimes.All(t => t > since))
                return Results.StatusCode(429);

            var lastOnMod = await db.Comments
                .Where(c => c.AccountId == account.Id && c.ModId == id)
                .OrderByDescending(c => c.Id)
                .Select(c => c.Body)
                .FirstOrDefaultAsync();
            if (lastOnMod == body) return Results.BadRequest("You already posted that comment.");

            var comment = new Comment
            {
                ModId = id,
                AccountId = account.Id,
                Body = body,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Comments.Add(comment);
            await db.SaveChangesAsync();

            return Results.Created($"/api/comments/{comment.Id}",
                new CommentDto(comment.Id, id, account.Username, comment.Body, comment.CreatedAt));
        });

        // Anyone signed in can report a comment; owners see the count in the console.
        app.MapPost("/api/comments/{id:int}/report", async (HttpContext ctx, AppDbContext db, int id) =>
        {
            var account = await ApiAuth.CurrentAsync(ctx, db);
            if (account is null) return Results.Unauthorized();

            var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == id);
            if (comment is null) return Results.NotFound();

            comment.Reports++;
            await db.SaveChangesAsync();
            return Results.Ok(new { comment.Id, comment.Reports });
        });

        app.MapDelete("/api/comments/{id:int}", async (HttpContext ctx, AppDbContext db, int id) =>
        {
            var account = await ApiAuth.CurrentAsync(ctx, db);
            if (account is null) return Results.Unauthorized();

            var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == id);
            if (comment is null) return Results.NotFound();
            if (comment.AccountId != account.Id && account.Role != AccountRole.Owner) return Results.StatusCode(403);

            db.Comments.Remove(comment);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        app.MapPost("/api/mods/{id:int}/reviews", async (HttpContext ctx, AppDbContext db, int id, ReviewRequest req) =>
        {
            var account = await ApiAuth.CurrentAsync(ctx, db);
            if (account is null) return Results.Unauthorized();
            if (req.Stars < 1 || req.Stars > 5) return Results.BadRequest("Stars must be between 1 and 5.");
            if (!await db.Mods.AnyAsync(m => m.Id == id)) return Results.NotFound();

            var review = await db.Reviews.FirstOrDefaultAsync(r => r.ModId == id && r.AccountId == account.Id);
            if (review is null)
            {
                review = new Review
                {
                    ModId = id,
                    AccountId = account.Id,
                    Stars = req.Stars,
                    Body = Util.Nullify(req.Body),
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.Reviews.Add(review);
            }
            else
            {
                review.Stars = req.Stars;
                review.Body = Util.Nullify(req.Body);
                review.CreatedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync();

            return Results.Ok(new ReviewDto(review.Id, id, account.Username, review.Stars, review.Body, review.CreatedAt));
        });

        app.MapDelete("/api/reviews/{id:int}", async (HttpContext ctx, AppDbContext db, int id) =>
        {
            var account = await ApiAuth.CurrentAsync(ctx, db);
            if (account is null) return Results.Unauthorized();

            var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == id);
            if (review is null) return Results.NotFound();
            if (review.AccountId != account.Id && account.Role != AccountRole.Owner) return Results.StatusCode(403);

            db.Reviews.Remove(review);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        app.MapPut("/api/mods/{id:int}/tags", async (HttpContext ctx, AppDbContext db, int id, ModTagsRequest req) =>
        {
            var account = await ApiAuth.CurrentAsync(ctx, db);
            if (account is null) return Results.Unauthorized();
            if (account.Role == AccountRole.User) return Results.StatusCode(403);

            var mod = await db.Mods.Include(m => m.Tags).FirstOrDefaultAsync(m => m.Id == id);
            if (mod is null) return Results.NotFound();
            if (mod.OwnerId != account.Id && account.Role != AccountRole.Owner) return Results.StatusCode(403);
            if (mod.Locked && account.Role != AccountRole.Owner) return Results.StatusCode(403);

            var wanted = TagUtil.Clean(req.Tags);

            var existing = await db.Tags.Where(t => wanted.Contains(t.Name)).ToListAsync();
            var existingNames = existing.Select(t => t.Name).ToHashSet();
            var toCreate = wanted
                .Where(n => !existingNames.Contains(n))
                .Select(n => new Tag { Name = n, IsPreset = false, CreatedAt = DateTimeOffset.UtcNow })
                .ToList();
            if (toCreate.Count > 0) db.Tags.AddRange(toCreate);

            mod.Tags.Clear();
            foreach (var t in existing.Concat(toCreate)) mod.Tags.Add(t);
            mod.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            var row = await db.Mods.Where(m => m.Id == id).Select(Projections.RowExpr).FirstAsync();
            return Results.Ok(Projections.ToDto(row));
        });
    }
}
