using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuMikuLoader.App.Models;
using MikuMikuLoader.App.Services;

namespace MikuMikuLoader.App.ViewModels;

public partial class BrowseViewModel : ViewModelBase
{
    private readonly ApiClient _api;
    private readonly InstallService _install;
    private readonly Action<string> _setGlobalStatus;
    private readonly Action<ModDto> _openDetail;
    private readonly string? _kind;

    public BrowseViewModel(ApiClient api, InstallService install, Action<string> setGlobalStatus, Action<ModDto> openDetail, string? kind = null)
    {
        _api = api;
        _install = install;
        _setGlobalStatus = setGlobalStatus;
        _openDetail = openDetail;
        _kind = kind;
    }

    public ObservableCollection<ModItemViewModel> Mods { get; } = new();

    public List<string> Sorts { get; } = new() { "newest", "downloads", "name", "rating" };

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _selectedSort = "downloads";
    [ObservableProperty] private bool _trustedOnly;
    [ObservableProperty] private bool _verifiedOnly;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isEmpty;

    partial void OnSelectedSortChanged(string value) => _ = ReloadAsync();
    partial void OnTrustedOnlyChanged(bool value) => _ = ReloadAsync();

    [RelayCommand]
    public async Task ReloadAsync()
    {
        IsLoading = true;
        StatusText = "Loading…";
        _setGlobalStatus("Loading mods…");
        try
        {
            var mods = await _api.GetModsAsync(SearchText, SelectedSort, TrustedOnly, VerifiedOnly, null, _kind);

            Mods.Clear();
            foreach (var m in mods)
                Mods.Add(new ModItemViewModel(m, _install, _setGlobalStatus, _openDetail));

            IsEmpty = mods.Count == 0;
            StatusText = mods.Count == 0
                ? (TrustedOnly ? "No trusted mods found." : "No mods found.")
                : $"Showing {mods.Count} mod{(mods.Count == 1 ? "" : "s")}.";
            _setGlobalStatus(StatusText);
        }
        catch (Exception ex)
        {
            Mods.Clear();
            IsEmpty = true;
            StatusText = "Couldn't reach the server. Check the URL in Settings.";
            _setGlobalStatus($"Offline — {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task Search() => ReloadAsync();
}
