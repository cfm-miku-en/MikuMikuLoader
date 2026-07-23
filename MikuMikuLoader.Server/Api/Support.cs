using System.Linq.Expressions;
using System.Security.Cryptography;
using MikuMikuLoader.Server.Data;
using MikuMikuLoader.Server.Dtos;
using Microsoft.EntityFrameworkCore;

namespace MikuMikuLoader.Server.Services;

public static class PasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out var iterations)) return false;

        var salt = Convert.FromBase64String(parts[1]);
        var key = Convert.FromBase64String(parts[2]);
        var test = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, key.Length);
        return CryptographicOperations.FixedTimeEquals(test, key);
    }
}

public static class Util
{
    public static string? Nullify(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static string[] SplitCsv(string? s) =>
        string.IsNullOrWhiteSpace(s)
            ? Array.Empty<string>()
            : s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}

public static class ApiAuth
{
    public static string? BearerToken(HttpContext ctx)
    {
        string? header = ctx.Request.Headers.Authorization;
        if (string.IsNullOrWhiteSpace(header)) return null;
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : null;
    }

    public static async Task<Account?> CurrentAsync(HttpContext ctx, AppDbContext db)
    {
        var token = BearerToken(ctx);
        if (token is null) return null;

        var session = await db.Sessions.Include(s => s.Account)
            .FirstOrDefaultAsync(s => s.Token == token);

        if (session is null || session.ExpiresAt < DateTimeOffset.UtcNow) return null;
        if (session.Account.IsBanned) return null;
        return session.Account;
    }

    public static async Task<string> IssueTokenAsync(AppDbContext db, Account account)
    {
        var token = Util.NewToken();
        db.Sessions.Add(new Session
        {
            Token = token,
            AccountId = account.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        });
        await db.SaveChangesAsync();
        return token;
    }
}

public static class Projections
{

    public static readonly Expression<Func<Mod, ModRow>> RowExpr = m => new ModRow(
        m.Id, m.Name, m.Version, m.Author, m.Description,
        m.DependenciesCsv, m.FileName, m.FileSizeBytes, m.Sha256, m.Downloads,
        m.Status,
        m.Owner.Username,
        m.Trusted,
        m.Verified,
        m.Kind,
        m.FeaturedUntil,
        m.RepoUrl,
        m.Images.Count,
        m.Reviews.Count == 0 ? 0.0 : m.Reviews.Average(r => (double)r.Stars),
        m.Reviews.Count,
        m.Comments.Count,
        m.Tags.OrderBy(t => t.Name).Select(t => t.Name).ToList(),
        m.CreatedAt);

    public static ModDto ToDto(ModRow r) => new(
        r.Id, r.Name, r.Version, r.Author, r.Description,
        Util.SplitCsv(r.DependenciesCsv), r.FileName, r.FileSizeBytes, r.Sha256, r.Downloads,
        r.Status.ToString(), r.OwnerUsername, r.Trusted, r.Verified,
        r.Kind.ToString(),
        r.FeaturedUntil.HasValue && r.FeaturedUntil.Value > DateTimeOffset.UtcNow,
        r.RepoUrl,
        r.ImageCount,
        Math.Round(r.Rating, 2), r.RatingCount, r.CommentCount, r.Tags.ToArray(), r.CreatedAt);
}

public static class Mapper
{
    public static AccountDto Account(Account a)
    {
        var owner = a.Role == AccountRole.Owner;
        var moderate = owner || a.CanModerate;
        var manageMods = owner || a.CanManageMods;
        var verify = owner || a.CanVerify;
        var postNews = owner || a.CanPostNews;
        return new AccountDto(
            a.Id, a.Username, a.Role.ToString(), a.TrustStatus.ToString(),
            a.TrustStatus == TrustStatus.Trusted, a.IsBanned,
            moderate, manageMods, verify, postNews,
            moderate || manageMods || verify || postNews,
            a.CreatedAt);
    }

    public static AnnouncementDto Announcement(Announcement a) => new(
        a.Id, a.Title, a.Body, a.Kind.ToString(), a.Published, a.CreatedAt, a.UpdatedAt);
}

public static class TagUtil
{
    public const int MaxPerMod = 10;
    public const int MaxLength = 30;

    public static string Normalize(string raw) => raw.Trim().ToLowerInvariant();

    public static bool IsValid(string normalized) =>
        normalized.Length is >= 1 and <= MaxLength;

    public static List<string> Clean(IEnumerable<string>? raw) =>
        (raw ?? Enumerable.Empty<string>())
            .Select(Normalize)
            .Where(IsValid)
            .Distinct()
            .Take(MaxPerMod)
            .ToList();
}

public static class OwnerBootstrap
{
    public static async Task EnsureOwnerAsync(AppDbContext db, IConfiguration cfg)
    {
        var user = cfg["Owner:Username"];
        var pass = cfg["Owner:Password"];
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass)) return;
        if (await db.Accounts.AnyAsync(a => a.Role == AccountRole.Owner)) return;

        db.Accounts.Add(new Account
        {
            Username = user.Trim(),
            PasswordHash = PasswordHasher.Hash(pass),
            Role = AccountRole.Owner,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
