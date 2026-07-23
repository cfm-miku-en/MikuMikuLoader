using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuMikuLoader.App.Models;
using Avalonia.Media.Imaging;
using MikuMikuLoader.App.Services;

namespace MikuMikuLoader.App.ViewModels;

public partial class ModDetailViewModel : ViewModelBase
{
    private readonly ApiClient _api;
    private readonly SessionService _session;
    private readonly Action _back;
    private readonly Action _goLogin;

    public ModDetailViewModel(ApiClient api, SessionService session, ModDto mod, Action back, Action goLogin)
    {
        _api = api;
        _session = session;
        _back = back;
        _goLogin = goLogin;
        Mod = mod;
        _ = LoadAsync();
    }

    public ModDto Mod { get; }

    public string Title => Mod.Name;
    public string Subtitle => $"v{Mod.Version} | by {Mod.Author} | {Mod.Downloads} downloads";
    public string Description =>
        string.IsNullOrWhiteSpace(Mod.Description) ? "No description provided." : Mod.Description;
    public string TagsText => Mod.Tags.Length == 0 ? "" : "Tags: " + string.Join(", ", Mod.Tags);
    public bool HasTags => Mod.Tags.Length > 0;

    public bool HasImages => Images.Count > 0;
    public bool HasRepo => !string.IsNullOrWhiteSpace(Mod.RepoUrl);
    public string RepoUrl => Mod.RepoUrl;

    public bool IsLoggedIn => _session.IsLoggedIn;
    public bool IsGuest => !_session.IsLoggedIn;

    public List<int> StarOptions { get; } = new() { 5, 4, 3, 2, 1 };

    public ObservableCollection<CommentDto> Comments { get; } = new();
    public ObservableCollection<ReviewItemViewModel> Reviews { get; } = new();
    public ObservableCollection<Bitmap> Images { get; } = new();

    [ObservableProperty] private string _ratingSummary = "Loading…";
    [ObservableProperty] private string _newComment = "";
    [ObservableProperty] private int _newStars = 5;
    [ObservableProperty] private string _reviewBody = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _hasNoComments;
    [ObservableProperty] private bool _hasNoReviews = true;


    private async Task LoadAsync()
    {
        if (Mod.ImageCount > 0 && Images.Count == 0)
        {
            try
            {
                foreach (var imageId in await _api.GetImageIdsAsync(Mod.Id))
                {
                    var bytes = await _api.GetImageBytesAsync(imageId);
                    if (bytes is not { Length: > 0 }) continue;
                    using var ms = new MemoryStream(bytes);
                    Images.Add(new Bitmap(ms));
                }
                OnPropertyChanged(nameof(HasImages));
            }
            catch {  }
        }

        try
        {
            var reviews = await _api.GetReviewsAsync(Mod.Id);
            ApplyReviews(reviews);
        }
        catch { RatingSummary = "Ratings unavailable."; }

        try
        {
            var comments = await _api.GetCommentsAsync(Mod.Id);
            Comments.Clear();
            foreach (var c in comments) Comments.Add(c);
            HasNoComments = comments.Count == 0;
        }
        catch (Exception ex) { Status = $"Couldn't load comments: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task PostCommentAsync()
    {
        if (string.IsNullOrWhiteSpace(NewComment)) return;
        try
        {
            var comment = await _api.PostCommentAsync(Mod.Id, NewComment.Trim());
            Comments.Insert(0, comment);
            HasNoComments = false;
            NewComment = "";
            Status = "Comment posted.";
        }
        catch (Exception ex) { Status = $"Couldn't post: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task SubmitRatingAsync()
    {
        try
        {
            var hadBody = !string.IsNullOrWhiteSpace(ReviewBody);
            await _api.PostReviewAsync(Mod.Id, NewStars, hadBody ? ReviewBody.Trim() : null);
            var reviews = await _api.GetReviewsAsync(Mod.Id);
            ApplyReviews(reviews);
            ReviewBody = "";
            Status = hadBody ? "Review posted." : "Rating submitted.";
        }
        catch (Exception ex) { Status = $"Couldn't rate: {ex.Message}"; }
    }

    private void ApplyReviews(ReviewsResult reviews)
    {
        RatingSummary = reviews.Count == 0
            ? "No ratings yet."
            : $"Rated {reviews.Rating:0.0} from {reviews.Count} rating(s).";

        Reviews.Clear();
        foreach (var r in reviews.Reviews.Where(r => !string.IsNullOrWhiteSpace(r.Body)))
            Reviews.Add(new ReviewItemViewModel(r));
        HasNoReviews = Reviews.Count == 0;
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    private void OpenRepo()
    {
        if (!HasRepo) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Mod.RepoUrl) { UseShellExecute = true });
        }
        catch {  }
    }

    [RelayCommand]
    private void Back() => _back();

    [RelayCommand]
    private void GoLogin() => _goLogin();
}

public class ReviewItemViewModel
{
    public ReviewItemViewModel(ReviewDto dto)
    {
        Author = dto.Author;
        Body = dto.Body ?? "";
        Stars = $"{dto.Stars}/5";
    }

    public string Author { get; }
    public string Body { get; }
    public string Stars { get; }
}
