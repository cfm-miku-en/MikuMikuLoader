using MikuMikuLoader.Server.Data;
using MikuMikuLoader.Server.Dtos;
using MikuMikuLoader.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace MikuMikuLoader.Server.Endpoints;

public static class OwnerEndpoints
{
    public static void MapOwnerEndpoints(this IEndpointRouteBuilder app)
    {

        app.MapGet("/api/owner/accounts", async (HttpContext ctx, AppDbContext db) =>
        {
            var (err, _) = await RequireAnyStaff(ctx, db);
            if (err is not null) return err;

            var accounts = await db.Accounts.OrderBy(a => a.Id).Select(a => new
            {
                a.Id,
                a.Username,
                Role = a.Role.ToString(),
                TrustStatus = a.TrustStatus.ToString(),
                a.IsBanned,
                a.BanReason,
                a.CanModerate,
                a.CanManageMods,
                a.CanVerify,
                a.CanPostNews,
                ModCount = a.Mods.Count,
                a.CreatedAt
            }).ToListAsync();

            return Results.Ok(accounts);
        });

        app.MapGet("/api/owner/trusted-applications", async (HttpContext ctx, AppDbContext db) =>
        {
            var (err, _) = await RequireVerify(ctx, db);
            if (err is not null) return err;

            var pending = await db.Accounts
                .Where(a => a.TrustStatus == TrustStatus.Pending)
                .OrderBy(a => a.Id)
                .ToListAsync();
            return Results.Ok(pending.Select(Mapper.Account));
        });

        app.MapPost("/api/owner/accounts/{id:int}/trust", async (HttpContext ctx, AppDbContext db, int id, TrustDecisionRequest req) =>
        {
            var (err, _) = await RequireVerify(ctx, db);
            if (err is not null) return err;

            var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id);
            if (account is null) return Results.NotFound();

            account.TrustStatus = req.Approve ? TrustStatus.Trusted : TrustStatus.Rejected;
            await db.SaveChangesAsync();
            return Results.Ok(Mapper.Account(account));
        });

        app.MapPost("/api/owner/accounts/{id:int}/ban", async (HttpContext ctx, AppDbContext db, int id, BanRequest req) =>
        {
            var (err, staff) = await RequireModerate(ctx, db);
            if (err is not null) return err;

            var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id);
            if (account is null) return Results.NotFound();
            if (account.Role == AccountRole.Owner) return Results.BadRequest("You can't ban an owner.");
            if (account.Id == staff!.Id) return Results.BadRequest("You can't ban yourself.");

            // Only the owner may act on other staff members.
            var targetIsStaff = account.CanModerate || account.CanManageMods || account.CanVerify || account.CanPostNews;
            if (targetIsStaff && staff.Role != AccountRole.Owner)
                return Results.StatusCode(403);

            account.IsBanned = true;
            account.BanReason = Util.Nullify(req.Reason);
            db.Sessions.RemoveRange(db.Sessions.Where(s => s.AccountId == id));
            await db.SaveChangesAsync();
            return Results.Ok(Mapper.Account(account));
        });

        app.MapPost("/api/owner/accounts/{id:int}/unban", async (HttpContext ctx, AppDbContext db, int id) =>
        {
            var (err, _) = await RequireModerate(ctx, db);
            if (err is not null) return err;

            var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id);
            if (account is null) return Results.NotFound();

            account.IsBanned = false;
            account.BanReason = null;
            await db.SaveChangesAsync();
            return Results.Ok(Mapper.Account(account));
        });

        app.MapGet("/api/owner/mods", async (HttpContext ctx, AppDbContext db) =>
        {
            var (err, _) = await RequireAnyStaff(ctx, db);
            if (err is not null) return err;

            var rows = await db.Mods.OrderByDescending(m => m.Id).Select(m => new
            {
                m.Id,
                m.Name,
                m.Version,
                Owner = m.Owner.Username,
                Status = m.Status.ToString(),
                m.Downloads,
                Rating = m.Reviews.Count == 0 ? 0.0 : m.Reviews.Average(r => (double)r.Stars),
                RatingCount = m.Reviews.Count,
                OneStars = m.Reviews.Count(r => r.Stars == 1),
                Kind = m.Kind.ToString(),
                m.FeaturedUntil,
                m.Trusted,
                m.Verified,
                m.Locked,
                Tags = m.Tags.OrderBy(t => t.Name).Select(t => t.Name).ToList(),
                m.CreatedAt
            }).ToListAsync();

            var shaped = rows.Select(r => new
            {
                r.Id, r.Name, r.Version, r.Owner, r.Status, r.Downloads,
                Rating = Math.Round(r.Rating, 2), r.RatingCount, r.OneStars, r.CreatedAt
            });
            return Results.Ok(shaped);
        });

        app.MapPost("/api/owner/mods/{id:int}/status", async (HttpContext ctx, AppDbContext db, int id, ModStatusRequest req) =>
        {
            var (err, _) = await RequireManageMods(ctx, db);
            if (err is not null) return err;

            if (!Enum.TryParse<ModStatus>(req.Status, ignoreCase: true, out var status))
                return Results.BadRequest("Invalid status. Use Published, InReview, or Removed.");

            var mod = await db.Mods.FirstOrDefaultAsync(m => m.Id == id);
            if (mod is null) return Results.NotFound();

            mod.Status = status;
            mod.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { mod.Id, status = mod.Status.ToString() });
        });

        // Feature/pin a mod on the front page. Days > 0 features until then;
        // a big value (e.g. 3650) acts as a pin; null/0 clears it.
        app.MapPost("/api/owner/mods/{id:int}/feature", async (HttpContext ctx, AppDbContext db, int id, FeatureRequest req) =>
        {
            var (err, _) = await RequireManageMods(ctx, db);
            if (err is not null) return err;

            var mod = await db.Mods.FirstOrDefaultAsync(m => m.Id == id);
            if (mod is null) return Results.NotFound();

            mod.FeaturedUntil = req.Days is > 0 ? DateTimeOffset.UtcNow.AddDays(req.Days.Value) : null;
            await db.SaveChangesAsync();
            return Results.Ok(new { mod.Id, mod.FeaturedUntil });
        });

        // Comment moderation. Owners can list every comment (most-reported first),
        // hide/unhide individual comments, or delete them outright.
        app.MapGet("/api/owner/comments", async (HttpContext ctx, AppDbContext db) =>
        {
            var (err, _) = await RequireModerate(ctx, db);
            if (err is not null) return err;

            var rows = await db.Comments
                .OrderByDescending(c => c.Id)
                .Select(c => new
                {
                    c.Id,
                    c.ModId,
                    ModName = c.Mod.Name,
                    Author = c.Account.Username,
                    AuthorId = c.AccountId,
                    AuthorBanned = c.Account.IsBanned,
                    c.Body,
                    c.Hidden,
                    c.Reports,
                    c.CreatedAt
                })
                .ToListAsync();

            var ordered = rows.OrderByDescending(c => c.Reports).ThenByDescending(c => c.Id).ToList();
            return Results.Ok(ordered);
        });

        app.MapPost("/api/owner/comments/{id:int}/hide", async (HttpContext ctx, AppDbContext db, int id, CommentHideRequest req) =>
        {
            var (err, _) = await RequireModerate(ctx, db);
            if (err is not null) return err;

            var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == id);
            if (comment is null) return Results.NotFound();

            comment.Hidden = req.Hidden;
            await db.SaveChangesAsync();
            return Results.Ok(new { comment.Id, comment.Hidden });
        });

        app.MapDelete("/api/owner/comments/{id:int}", async (HttpContext ctx, AppDbContext db, int id) =>
        {
            var (err, _) = await RequireModerate(ctx, db);
            if (err is not null) return err;

            var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == id);
            if (comment is null) return Results.NotFound();

            db.Comments.Remove(comment);
            await db.SaveChangesAsync();
            return Results.Ok(new { deleted = id });
        });

        // Wipe every comment by one account - for spam floods.
        app.MapDelete("/api/owner/accounts/{id:int}/comments", async (HttpContext ctx, AppDbContext db, int id) =>
        {
            var (err, _) = await RequireModerate(ctx, db);
            if (err is not null) return err;

            var comments = await db.Comments.Where(c => c.AccountId == id).ToListAsync();
            db.Comments.RemoveRange(comments);
            await db.SaveChangesAsync();
            return Results.Ok(new { deleted = comments.Count });
        });

        // Per-mod trust. Owner reviews each mod individually so a trusted developer
        // can't auto-publish a malicious mod under the trusted badge.
        app.MapPost("/api/owner/mods/{id:int}/trust", async (HttpContext ctx, AppDbContext db, int id, ModTrustRequest req) =>
        {
            var (err, _) = await RequireVerify(ctx, db);
            if (err is not null) return err;

            var mod = await db.Mods.FirstOrDefaultAsync(m => m.Id == id);
            if (mod is null) return Results.NotFound();

            mod.Trusted = req.Trusted;
            mod.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { mod.Id, mod.Trusted });
        });

        // Verified is a lighter badge than Trusted: it means staff checked the mod
        // and it's legitimate. A mod can be verified without being trusted.
        app.MapPost("/api/owner/mods/{id:int}/verify", async (HttpContext ctx, AppDbContext db, int id, ModVerifyRequest req) =>
        {
            var (err, _) = await RequireVerify(ctx, db);
            if (err is not null) return err;

            var mod = await db.Mods.FirstOrDefaultAsync(m => m.Id == id);
            if (mod is null) return Results.NotFound();

            mod.Verified = req.Verified;
            mod.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { mod.Id, mod.Verified });
        });

        // Lock a mod so its developer can't upload new versions or edit it. Use when a
        // dev account may be compromised; pair with status = Removed to hide it too.
        app.MapPost("/api/owner/mods/{id:int}/lock", async (HttpContext ctx, AppDbContext db, int id, ModLockRequest req) =>
        {
            var (err, _) = await RequireManageMods(ctx, db);
            if (err is not null) return err;

            var mod = await db.Mods.FirstOrDefaultAsync(m => m.Id == id);
            if (mod is null) return Results.NotFound();

            mod.Locked = req.Locked;
            mod.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { mod.Id, mod.Locked });
        });

        app.MapDelete("/api/owner/mods/{id:int}", async (HttpContext ctx, AppDbContext db, ModFileStore files, int id) =>
        {
            var (err, _) = await RequireManageMods(ctx, db);
            if (err is not null) return err;

            var mod = await db.Mods.FirstOrDefaultAsync(m => m.Id == id);
            if (mod is null) return Results.NotFound();

            db.Mods.Remove(mod);
            await db.SaveChangesAsync();
            files.DeleteMod(id);
            return Results.NoContent();
        });

        // Only the owner can grant or revoke staff permissions.
        app.MapPost("/api/owner/accounts/{id:int}/permissions", async (HttpContext ctx, AppDbContext db, int id, PermissionsRequest req) =>
        {
            var (err, _) = await RequireOwner(ctx, db);
            if (err is not null) return err;

            var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id);
            if (account is null) return Results.NotFound();
            if (account.Role == AccountRole.Owner) return Results.BadRequest("The owner already has every permission.");

            if (req.CanModerate is { } m) account.CanModerate = m;
            if (req.CanManageMods is { } mm) account.CanManageMods = mm;
            if (req.CanVerify is { } v) account.CanVerify = v;
            if (req.CanPostNews is { } n) account.CanPostNews = n;

            await db.SaveChangesAsync();
            return Results.Ok(new
            {
                account.Id,
                account.CanModerate,
                account.CanManageMods,
                account.CanVerify,
                account.CanPostNews
            });
        });

        app.MapGet("/api/owner/stats", async (HttpContext ctx, AppDbContext db) =>
        {
            var (err, _) = await RequireAnyStaff(ctx, db);
            if (err is not null) return err;

            return Results.Ok(new
            {
                accounts = await db.Accounts.CountAsync(),
                developers = await db.Accounts.CountAsync(a => a.Role == AccountRole.Developer),
                banned = await db.Accounts.CountAsync(a => a.IsBanned),
                pendingTrust = await db.Accounts.CountAsync(a => a.TrustStatus == TrustStatus.Pending),
                mods = await db.Mods.CountAsync(),
                published = await db.Mods.CountAsync(m => m.Status == ModStatus.Published),
                inReview = await db.Mods.CountAsync(m => m.Status == ModStatus.InReview),
                removed = await db.Mods.CountAsync(m => m.Status == ModStatus.Removed),
                comments = await db.Comments.CountAsync(),
                reviews = await db.Reviews.CountAsync()
            });
        });

        app.MapGet("/api/owner/announcements", async (HttpContext ctx, AppDbContext db) =>
        {
            var (err, _) = await RequirePostNews(ctx, db);
            if (err is not null) return err;

            var list = await db.Announcements.OrderByDescending(a => a.Id).ToListAsync();
            return Results.Ok(list.Select(Mapper.Announcement));
        });

        app.MapPost("/api/owner/announcements", async (HttpContext ctx, AppDbContext db, AnnouncementRequest req) =>
        {
            var (err, _) = await RequirePostNews(ctx, db);
            if (err is not null) return err;
            if (string.IsNullOrWhiteSpace(req.Title)) return Results.BadRequest("Title is required.");

            var kind = Enum.TryParse<AnnouncementKind>(req.Kind, ignoreCase: true, out var k) ? k : AnnouncementKind.News;
            var now = DateTimeOffset.UtcNow;
            var announcement = new Announcement
            {
                Title = req.Title.Trim(),
                Body = req.Body ?? "",
                Kind = kind,
                Published = req.Published ?? true,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Announcements.Add(announcement);
            await db.SaveChangesAsync();
            return Results.Created($"/api/announcements/{announcement.Id}", Mapper.Announcement(announcement));
        });

        app.MapPut("/api/owner/announcements/{id:int}", async (HttpContext ctx, AppDbContext db, int id, AnnouncementRequest req) =>
        {
            var (err, _) = await RequirePostNews(ctx, db);
            if (err is not null) return err;

            var announcement = await db.Announcements.FirstOrDefaultAsync(a => a.Id == id);
            if (announcement is null) return Results.NotFound();

            if (!string.IsNullOrWhiteSpace(req.Title)) announcement.Title = req.Title.Trim();
            if (req.Body is not null) announcement.Body = req.Body;
            if (Enum.TryParse<AnnouncementKind>(req.Kind, ignoreCase: true, out var k)) announcement.Kind = k;
            if (req.Published.HasValue) announcement.Published = req.Published.Value;
            announcement.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(Mapper.Announcement(announcement));
        });

        app.MapDelete("/api/owner/announcements/{id:int}", async (HttpContext ctx, AppDbContext db, int id) =>
        {
            var (err, _) = await RequirePostNews(ctx, db);
            if (err is not null) return err;

            var announcement = await db.Announcements.FirstOrDefaultAsync(a => a.Id == id);
            if (announcement is null) return Results.NotFound();

            db.Announcements.Remove(announcement);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        app.MapGet("/api/owner/tags", async (HttpContext ctx, AppDbContext db) =>
        {
            var (err, _) = await RequireManageMods(ctx, db);
            if (err is not null) return err;

            var tags = await db.Tags
                .OrderByDescending(t => t.IsPreset).ThenBy(t => t.Name)
                .Select(t => new TagDto(t.Id, t.Name, t.IsPreset, t.Mods.Count))
                .ToListAsync();
            return Results.Ok(tags);
        });

        app.MapPost("/api/owner/tags", async (HttpContext ctx, AppDbContext db, TagCreateRequest req) =>
        {
            var (err, _) = await RequireManageMods(ctx, db);
            if (err is not null) return err;

            var name = TagUtil.Normalize(req.Name ?? "");
            if (!TagUtil.IsValid(name)) return Results.BadRequest($"Tag must be 1-{TagUtil.MaxLength} characters.");

            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == name);
            if (tag is null)
            {
                tag = new Tag { Name = name, IsPreset = true, CreatedAt = DateTimeOffset.UtcNow };
                db.Tags.Add(tag);
            }
            else
            {
                tag.IsPreset = true;
            }
            await db.SaveChangesAsync();

            var modCount = await db.Tags.Where(t => t.Id == tag.Id).Select(t => t.Mods.Count).FirstAsync();
            return Results.Ok(new TagDto(tag.Id, tag.Name, tag.IsPreset, modCount));
        });

        app.MapDelete("/api/owner/tags/{id:int}", async (HttpContext ctx, AppDbContext db, int id) =>
        {
            var (err, _) = await RequireManageMods(ctx, db);
            if (err is not null) return err;

            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == id);
            if (tag is null) return Results.NotFound();

            db.Tags.Remove(tag);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static async Task<(IResult? Error, Account? Staff)> RequireOwner(HttpContext ctx, AppDbContext db)
    {
        var account = await ApiAuth.CurrentAsync(ctx, db);
        if (account is null) return (Results.Unauthorized(), null);
        if (account.Role != AccountRole.Owner) return (Results.StatusCode(403), null);
        return (null, account);
    }

    // Permission gate. The owner always passes; everyone else needs the specific flag.
    private static async Task<(IResult? Error, Account? Staff)> Require(
        HttpContext ctx, AppDbContext db, Func<Account, bool> permission)
    {
        var account = await ApiAuth.CurrentAsync(ctx, db);
        if (account is null) return (Results.Unauthorized(), null);
        if (account.Role == AccountRole.Owner) return (null, account);
        if (!permission(account)) return (Results.StatusCode(403), null);
        return (null, account);
    }

    private static Task<(IResult? Error, Account? Staff)> RequireModerate(HttpContext ctx, AppDbContext db) =>
        Require(ctx, db, a => a.CanModerate);

    private static Task<(IResult? Error, Account? Staff)> RequireManageMods(HttpContext ctx, AppDbContext db) =>
        Require(ctx, db, a => a.CanManageMods);

    private static Task<(IResult? Error, Account? Staff)> RequireVerify(HttpContext ctx, AppDbContext db) =>
        Require(ctx, db, a => a.CanVerify);

    private static Task<(IResult? Error, Account? Staff)> RequirePostNews(HttpContext ctx, AppDbContext db) =>
        Require(ctx, db, a => a.CanPostNews);

    // Any staff permission at all - used for read-only views like stats and lists.
    private static Task<(IResult? Error, Account? Staff)> RequireAnyStaff(HttpContext ctx, AppDbContext db) =>
        Require(ctx, db, a => a.CanModerate || a.CanManageMods || a.CanVerify || a.CanPostNews);
}
