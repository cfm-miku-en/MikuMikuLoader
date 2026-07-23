namespace MikuMikuLoader.App.Models;

public class ServerInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Software { get; set; } = "";
    public string SoftwareVersion { get; set; } = "";
    public int ProtocolVersion { get; set; }
    public int ModCount { get; set; }
    public bool AllowUploads { get; set; }
}

public class ModDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string[] Dependencies { get; set; } = Array.Empty<string>();
    public string FileName { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public int Downloads { get; set; }
    public string Status { get; set; } = "";
    public string OwnerUsername { get; set; } = "";
    public bool Trusted { get; set; }
    public bool Verified { get; set; }
    public string RepoUrl { get; set; } = "";
    public int ImageCount { get; set; }
    public string Kind { get; set; } = "Mod";
    public bool Featured { get; set; }
    public double Rating { get; set; }
    public int RatingCount { get; set; }
    public int CommentCount { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
    public DateTimeOffset CreatedAt { get; set; }
}

public class TagDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsPreset { get; set; }
    public int ModCount { get; set; }
}

public class AnnouncementDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool Published { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class Account
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Role { get; set; } = "";
    public string TrustStatus { get; set; } = "";
    public bool Trusted { get; set; }
    public bool IsBanned { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool CanModerate { get; set; }
    public bool CanManageMods { get; set; }
    public bool CanVerify { get; set; }
    public bool CanPostNews { get; set; }
    public bool IsStaff { get; set; }
}

public class AuthResponse
{
    public string Token { get; set; } = "";
    public Account Account { get; set; } = new();
}

public class CommentDto
{
    public int Id { get; set; }
    public int ModId { get; set; }
    public string Author { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public class ReviewDto
{
    public int Id { get; set; }
    public int ModId { get; set; }
    public string Author { get; set; } = "";
    public int Stars { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ReviewsResult
{
    public double Rating { get; set; }
    public int Count { get; set; }
    public List<ReviewDto> Reviews { get; set; } = new();
}
