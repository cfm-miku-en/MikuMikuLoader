using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuMikuLoader.App.Models;
using MikuMikuLoader.App.Services;

namespace MikuMikuLoader.App.ViewModels;

public partial class NewsViewModel : ViewModelBase
{
    private readonly ApiClient _api;
    private readonly Action<string> _setStatus;

    public NewsViewModel(ApiClient api, Action<string> setStatus)
    {
        _api = api;
        _setStatus = setStatus;
    }

    public ObservableCollection<NewsItemViewModel> Posts { get; } = new();

    [ObservableProperty] private bool _hasMotd;
    [ObservableProperty] private string _motdTitle = "";
    [ObservableProperty] private string _motdBody = "";
    [ObservableProperty] private string _motdDate = "";
    [ObservableProperty] private bool _hasNoNews;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _statusText = "";

    [RelayCommand]
    public async Task ReloadAsync()
    {
        StatusText = "Loading...";

        try
        {
            var motd = await _api.GetMotdAsync();
            HasMotd = motd is not null;
            if (motd is not null)
            {
                MotdTitle = motd.Title;
                MotdBody = motd.Body;
                MotdDate = Format(motd.CreatedAt);
            }
        }
        catch
        {
            HasMotd = false;
        }

        try
        {
            var news = await _api.GetAnnouncementsAsync("news");
            Posts.Clear();
            foreach (var n in news.OrderByDescending(a => a.CreatedAt))
                Posts.Add(new NewsItemViewModel(n));

            HasNoNews = Posts.Count == 0;
            IsEmpty = !HasMotd && Posts.Count == 0;
            StatusText = "";
        }
        catch (Exception ex)
        {
            Posts.Clear();
            HasNoNews = true;
            IsEmpty = !HasMotd;
            StatusText = "Couldn't load news. Check the server in Settings.";
            _setStatus($"News offline - {ex.Message}");
        }
    }

    private static string Format(DateTimeOffset d) => d.ToLocalTime().ToString("MMM d, yyyy");
}

public class NewsItemViewModel : ViewModelBase
{
    public NewsItemViewModel(AnnouncementDto a)
    {
        Title = a.Title;
        Body = a.Body;
        DateText = a.CreatedAt.ToLocalTime().ToString("MMM d, yyyy");
    }

    public string Title { get; }
    public string Body { get; }
    public string DateText { get; }
}
