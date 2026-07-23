using MikuMikuLoader.Server.Data;
using MikuMikuLoader.Server.Dtos;
using MikuMikuLoader.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace MikuMikuLoader.Server.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {

        app.MapPost("/api/auth/register", async (AppDbContext db, RegisterRequest req) =>
        {
            var username = (req.Username ?? "").Trim();
            if (username.Length < 3)
                return Results.BadRequest("Username must be at least 3 characters.");
            if ((req.Password ?? "").Length < 8)
                return Results.BadRequest("Password must be at least 8 characters.");
            if (await db.Accounts.AnyAsync(a => a.Username == username))
                return Results.Conflict("That username is taken.");

            var account = new Account
            {
                Username = username,
                PasswordHash = PasswordHasher.Hash(req.Password!),

                Role = req.AsDeveloper == true ? AccountRole.Developer : AccountRole.User,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Accounts.Add(account);
            await db.SaveChangesAsync();

            var token = await ApiAuth.IssueTokenAsync(db, account);
            return Results.Ok(new AuthResponse(token, Mapper.Account(account)));
        });

        app.MapPost("/api/auth/login", async (AppDbContext db, LoginRequest req) =>
        {
            var username = (req.Username ?? "").Trim();
            var account = await db.Accounts.FirstOrDefaultAsync(a => a.Username == username);

            if (account is null || !PasswordHasher.Verify(req.Password ?? "", account.PasswordHash))
                return Results.Json(new { error = "Invalid username or password." }, statusCode: 401);

            if (account.IsBanned)
                return Results.Json(new { error = "This account is banned.", reason = account.BanReason }, statusCode: 403);

            var token = await ApiAuth.IssueTokenAsync(db, account);
            return Results.Ok(new AuthResponse(token, Mapper.Account(account)));
        });

        app.MapPost("/api/auth/logout", async (HttpContext ctx, AppDbContext db) =>
        {
            var token = ApiAuth.BearerToken(ctx);
            if (token is not null)
            {
                var session = await db.Sessions.FirstOrDefaultAsync(s => s.Token == token);
                if (session is not null)
                {
                    db.Sessions.Remove(session);
                    await db.SaveChangesAsync();
                }
            }
            return Results.Ok();
        });

        app.MapGet("/api/auth/me", async (HttpContext ctx, AppDbContext db) =>
        {
            var account = await ApiAuth.CurrentAsync(ctx, db);
            return account is null ? Results.Unauthorized() : Results.Ok(Mapper.Account(account));
        });

        app.MapPost("/api/auth/become-developer", async (HttpContext ctx, AppDbContext db) =>
        {
            var account = await ApiAuth.CurrentAsync(ctx, db);
            if (account is null) return Results.Unauthorized();

            if (account.Role == AccountRole.User)
            {
                account.Role = AccountRole.Developer;
                await db.SaveChangesAsync();
            }
            return Results.Ok(Mapper.Account(account));
        });
    }
}
