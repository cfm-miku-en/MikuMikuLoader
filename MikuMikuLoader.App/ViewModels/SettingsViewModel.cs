using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuMikuLoader.App.Services;

namespace MikuMikuLoader.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    public const string DefaultServerUrl = "https://loader.mikuuu.xyz";

    private readonly ApiClient _api;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly InstallService _install;
    private readonly Action _onServerChanged;

    public SettingsViewModel(
        ApiClient api,
        SettingsService settingsService,
        AppSettings settings,
        InstallService install,
        Action onServerChanged)
    {
        _api = api;
        _settingsService = settingsService;
        _settings = settings;
        _install = install;
        _onServerChanged = onServerChanged;
        _serverUrl = settings.ServerUrl;
        UpdateGameFolderText();
    }

    [ObservableProperty] private string _serverUrl;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _gameFolder = "";
    [ObservableProperty] private string _gameFolderStatus = "";

    public string DefaultHint => $"Default: {DefaultServerUrl}";

    [RelayCommand]
    private async Task SaveAsync()
    {
        var url = (ServerUrl ?? "").Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            Status = "Enter a server URL first.";
            return;
        }

        _settings.ServerUrl = url;
        _settingsService.Save(_settings);
        _api.SetBaseUrl(url);

        IsBusy = true;
        Status = "Saved. Connecting…";
        try
        {
            var info = await _api.GetInfoAsync();
            Status = info is null
                ? "Saved, but the server didn't respond."
                : $"Connected to {info.Name} — {info.ModCount} mod(s) available.";
        }
        catch (Exception ex)
        {
            Status = $"Saved, but couldn't connect: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        _onServerChanged();
    }

    [RelayCommand]
    private void ResetToDefault()
    {
        ServerUrl = DefaultServerUrl;
        Status = "Reset to the default server. Click Save to apply.";
    }

    public void SetGameFolder(string folder)
    {
        _install.SetGameFolder(folder);
        UpdateGameFolderText();
        GameFolderStatus = "Gorilla Tag folder set.";
    }

    [RelayCommand]
    private void DetectGame()
    {
        var found = _install.Detect();
        UpdateGameFolderText();
        GameFolderStatus = found is null
            ? "Couldn't find Gorilla Tag automatically. Use Browse to pick it."
            : "Found Gorilla Tag automatically.";
    }

    private void UpdateGameFolderText() =>
        GameFolder = _install.GameFolder ?? "(not set)";
}
