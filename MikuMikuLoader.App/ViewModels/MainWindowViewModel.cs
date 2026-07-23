using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuMikuLoader.App.Models;
using MikuMikuLoader.App.Services;

namespace MikuMikuLoader.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ApiClient _api;
    private readonly SessionService _session;
    private readonly UpdateService _updates = new();

    public BrowseViewModel Browse { get; }
    public BrowseViewModel Reshades { get; }
    public LibraryViewModel Library { get; }
    public SettingsViewModel Settings { get; }
    public DevPanelViewModel DevPanel { get; }
    public LoginViewModel Login { get; }
    public NewsViewModel News { get; }

    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private string _serverStatus = "connecting...";
    [ObservableProperty] private string _statusBar = "Ready.";

    [ObservableProperty] private bool _isBrowseActive;
    [ObservableProperty] private bool _isReshadesActive;
    [ObservableProperty] private string _pageTitle = "Browse Online";
    [ObservableProperty] private string _pageSubtitle = "Find mods other people uploaded, or share your own.";
    [ObservableProperty] private bool _showMotd;
    [ObservableProperty] private string _motdTitle = "";
    [ObservableProperty] private string _motdBody = "";
    [ObservableProperty] private bool _isLibraryActive;
    [ObservableProperty] private bool _isSettingsActive;
    [ObservableProperty] private bool _isDeveloperActive;
    [ObservableProperty] private bool _isNewsActive;

    [ObservableProperty] private bool _isLoggedIn;
    [ObservableProperty] private bool _isDeveloper;
    [ObservableProperty] private bool _isOwner;
    [ObservableProperty] private bool _isStaff;
    [ObservableProperty] private string _accountName = "";

    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _updateNotice = "";
    private string _updateUrl = AppInfo.ReleasesPage;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;

    public MainWindowViewModel(
        ApiClient api,
        SettingsService settingsService,
        AppSettings settings,
        InstallService install,
        SessionService session)
    {
        _api = api;
        _session = session;
        _settingsService = settingsService;
        _settings = settings;
        _session.Changed += UpdateSessionUi;

        Browse = new BrowseViewModel(api, install, s => StatusBar = s, OpenModDetail, "mod");
        Reshades = new BrowseViewModel(api, install, s => StatusBar = s, OpenModDetail, "reshade");
        Library = new LibraryViewModel(install, s => StatusBar = s);
        Settings = new SettingsViewModel(api, settingsService, settings, install, OnServerChanged);
        DevPanel = new DevPanelViewModel(api, session, s => StatusBar = s);
        Login = new LoginViewModel(api, session, OnSignedIn);
        News = new NewsViewModel(api, s => StatusBar = s);

        ShowBrowse();
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        await _session.RestoreAsync();
        UpdateSessionUi();
        await RefreshServerInfoAsync();
        await CheckForUpdatesAsync();
        await ShowMotdIfNewAsync();
    }

    // Show the current MOTD once per new announcement, as a dismissible popup.
    private async Task ShowMotdIfNewAsync()
    {
        try
        {
            var motd = await _api.GetMotdAsync();
            if (motd is null || motd.Id == _settings.LastSeenMotdId) return;

            MotdTitle = motd.Title;
            MotdBody = motd.Body;
            ShowMotd = true;

            _settings.LastSeenMotdId = motd.Id;
            _settingsService.Save(_settings);
        }
        catch {  }
    }

    [RelayCommand]
    private void DismissMotd() => ShowMotd = false;

    private void UpdateSessionUi()
    {
        IsLoggedIn = _session.IsLoggedIn;
        IsDeveloper = _session.IsDeveloper;
        IsOwner = _session.IsOwner;
        IsStaff = _session.IsStaff;
        AccountName = _session.Account?.Username ?? "";
    }

    private async Task RefreshServerInfoAsync()
    {
        try
        {
            var info = await _api.GetInfoAsync();
            ServerStatus = info is null ? "offline" : info.Name;
        }
        catch { ServerStatus = "offline"; }
    }

    private async Task CheckForUpdatesAsync()
    {
        var info = await _updates.CheckAsync();
        if (info is not null)
        {
            UpdateAvailable = true;
            UpdateNotice = $"Update available: v{info.Version}";
            _updateUrl = info.Url;
        }
    }

    private async void OnServerChanged()
    {
        await RefreshServerInfoAsync();
        await Browse.ReloadAsync();
        ShowBrowse();
    }

    private void OnSignedIn()
    {
        UpdateSessionUi();
        ShowBrowse();
        StatusBar = $"Signed in as {AccountName}.";
    }

    private void OpenModDetail(ModDto mod)
    {
        CurrentPage = new ModDetailViewModel(_api, _session, mod, ShowBrowse, ShowLogin);
        SetActive();
    }

    [RelayCommand]
    private void ShowBrowse()
    {
        SetPage("Browse Online", "Find mods other people uploaded, or share your own.");
        CurrentPage = Browse;
        SetActive(browse: true);
        _ = Browse.ReloadAsync();
    }

    [RelayCommand]
    private void ShowReshades()
    {
        SetPage("Reshades", "Presets and shaders for Gorilla Tag. ReShade required.");
        CurrentPage = Reshades;
        SetActive(reshades: true);
        _ = Reshades.ReloadAsync();
    }

    [RelayCommand]
    private void ShowLibrary()
    {
        SetPage("Library", "Everything installed in your BepInEx plugins folder.");
        Library.Refresh();
        CurrentPage = Library;
        SetActive(library: true);
    }

    [RelayCommand]
    private void ShowSettings()
    {
        SetPage("Settings", "Server, game folder and app preferences.");
        CurrentPage = Settings;
        SetActive(settings: true);
    }

    [RelayCommand]
    private async Task ShowDeveloperAsync()
    {
        SetPage("Developer", "Upload and manage your own mods and reshades.");
        await DevPanel.RefreshAsync();
        CurrentPage = DevPanel;
        SetActive(developer: true);
    }

    [RelayCommand]
    private async Task ShowNewsAsync()
    {
        SetPage("News", "Announcements from the server owner.");
        await News.ReloadAsync();
        CurrentPage = News;
        SetActive(news: true);
    }

    [RelayCommand]
    private void ShowLogin()
    {
        CurrentPage = Login;
        SetActive();
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _session.SignOutAsync();
        StatusBar = "Signed out.";
        ShowBrowse();
    }

    [RelayCommand]
    private void OpenOwnerTools()
    {
        try { Process.Start(new ProcessStartInfo($"{_api.BaseUrl}/admin") { UseShellExecute = true }); }
        catch { }
    }

    [RelayCommand]
    private void OpenUpdate()
    {
        try { Process.Start(new ProcessStartInfo(_updateUrl) { UseShellExecute = true }); }
        catch {  }
    }

    private void SetPage(string title, string subtitle)
    {
        PageTitle = title;
        PageSubtitle = subtitle;
    }

    private void SetActive(bool browse = false, bool library = false, bool settings = false, bool developer = false, bool news = false, bool reshades = false)
    {
        IsBrowseActive = browse;
        IsReshadesActive = reshades;
        IsLibraryActive = library;
        IsSettingsActive = settings;
        IsDeveloperActive = developer;
        IsNewsActive = news;
    }
}
