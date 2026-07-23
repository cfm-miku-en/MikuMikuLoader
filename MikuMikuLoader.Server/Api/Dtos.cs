using MikuMikuLoader.Server.Data;

namespace MikuMikuLoader.Server.Dtos;

public record ServerInfo(
    string Name, string Description, string Software, string SoftwareVersion,
    int ProtocolVersion, int ModCount, bool AllowUploads);

public record RegisterRequest(string Username, string Password, bool? AsDeveloper = null);
public record LoginRequest(string Username, string Password);
public record AuthResponse(string Token, AccountDto Account);

public record AccountDto(
    int Id, string Username, string Role, string TrustStatus,
    bool IsTrusted, bool IsBanned,
    bool CanModerate, bool CanManageMods, bool CanVerify, bool CanPostNews, bool IsStaff,
    DateTimeOffset CreatedAt);

public record ModRow(
    int Id, string Name, string Version, string Author, string Description,
    string DependenciesCsv, string FileName, long FileSizeBytes, string Sha256, int Downloads,
    ModStatus Status, string OwnerUsername, bool Trusted, bool Verified, ModKind Kind, DateTimeOffset? FeaturedUntil,
    string RepoUrl, int ImageCount,
    double Rating, int RatingCount, int CommentCount, List<string> Tags, DateTimeOffset CreatedAt);

public record ModEditRequest(string? Version, string? Description, string[]? Dependencies, string? RepoUrl);

public record ModDto(
    int Id, string Name, string Version, string Author, string Description,
    string[] Dependencies, string FileName, long FileSizeBytes, string Sha256, int Downloads,
    string Status, string OwnerUsername, bool Trusted, bool Verified, string Kind, bool Featured,
    string RepoUrl, int ImageCount,
    double Rating, int RatingCount, int CommentCount, string[] Tags, DateTimeOffset CreatedAt);

public record CommentRequest(string Body);
public record CommentDto(int Id, int ModId, string Author, string Body, DateTimeOffset CreatedAt);

public record ReviewRequest(int Stars, string? Body);
public record ReviewDto(int Id, int ModId, string Author, int Stars, string? Body, DateTimeOffset CreatedAt);

public record TagDto(int Id, string Name, bool IsPreset, int ModCount);
public record TagCreateRequest(string Name);
public record ModTagsRequest(string[] Tags);

public record AnnouncementDto(
    int Id, string Title, string Body, string Kind, bool Published,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public record AnnouncementRequest(string Title, string Body, string? Kind, bool? Published);

public record BanRequest(string? Reason);
public record TrustDecisionRequest(bool Approve);
public record ModStatusRequest(string Status);
public record ModTrustRequest(bool Trusted);
public record ModVerifyRequest(bool Verified);
public record CommentHideRequest(bool Hidden);
public record PermissionsRequest(bool? CanModerate, bool? CanManageMods, bool? CanVerify, bool? CanPostNews);
public record ModLockRequest(bool Locked);
public record FeatureRequest(double? Days);
