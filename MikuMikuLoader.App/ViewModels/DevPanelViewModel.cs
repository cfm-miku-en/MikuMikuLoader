using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuMikuLoader.App.Models;
using MikuMikuLoader.App.Services;

namespace MikuMikuLoader.App.ViewModels;

public partial class DevPanelViewModel : ViewModelBase
{
    private readonly ApiClient _api;
    private readonly SessionService _session;
    private readonly Action<string> _setStatus;

    public DevPanelViewModel(ApiClient api, SessionService session, Action<string> setStatus)
    {
        _api = api;
        _session = session;
        _setStatus = setStatus;
        _session.Changed += () => { ApplyRole(); _ = LoadMineAsync(); };
        ApplyRole();
    }

    [ObservableProperty] private bool _isDeveloper;
    [ObservableProperty] private bool _isLoggedIn;
    [ObservableProperty] private string _roleMessage = "";
    [ObservableProperty] private string _trustStatus = "";
    [ObservableProperty] private bool _canApplyTrusted;

    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _version = "1.0.0";
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _dependencies = "";
    [ObservableProperty] private string _tags = "";
    [ObservableProperty] private string _uploadStatus = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasPresets;
    [ObservableProperty] private string _uploadKind = "Mod";
    [ObservableProperty] private string _repoUrl = "";
    [ObservableProperty] private string _imagePath = "";

    public List<string> ImagePaths { get; } = new();

    public bool IsModUpload => !string.Equals(UploadKind, "Reshade", StringComparison.OrdinalIgnoreCase);

    public string FileWatermark => IsModUpload
        ? "Path to your .dll or .zip"
        : "Path to your .zip or .ini";

    partial void OnUploadKindChanged(string value)
    {
        OnPropertyChanged(nameof(IsModUpload));
        OnPropertyChanged(nameof(FileWatermark));
    }

    public List<string> Kinds { get; } = new() { "Mod", "Reshade" };

    public ObservableCollection<DevModItemViewModel> Mods { get; } = new();
    public ObservableCollection<PresetTagViewModel> Presets { get; } = new();
    [ObservableProperty] private bool _hasNoMods;

    private void ApplyRole()
    {
        var acct = _session.Account;
        IsLoggedIn = acct is not null;
        IsDeveloper = _session.IsDeveloper;
        TrustStatus = acct?.TrustStatus ?? "";
        CanApplyTrusted = IsDeveloper && acct?.TrustStatus is "None" or "Rejected";

        RoleMessage = acct is null
            ? "Sign in to manage mods."
            : IsDeveloper
                ? $"Signed in as {acct.Username} | trust: {acct.TrustStatus}"
                : $"You're a User ({acct.Username}). Become a developer to upload mods.";

        if (string.IsNullOrEmpty(Author) && acct is not null) Author = acct.Username;
    }

    public async Task RefreshAsync()
    {
        ApplyRole();
        await LoadPresetsAsync();
        await LoadMineAsync();
    }

    private async Task LoadPresetsAsync()
    {
        try
        {
            var presets = await _api.GetTagsAsync(presetOnly: true);
            Presets.Clear();
            foreach (var t in presets)
                Presets.Add(new PresetTagViewModel(t.Name, AppendUploadTag));
            HasPresets = Presets.Count > 0;
        }
        catch { HasPresets = false; }
    }

    public void AppendUploadTag(string name)
    {
        var current = ParseTags(Tags).ToList();
        if (!current.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            current.Add(name);
            Tags = string.Join(", ", current);
        }
    }

    private async Task LoadMineAsync()
    {
        Mods.Clear();
        if (!IsDeveloper) { HasNoMods = false; return; }
        try
        {
            var mine = await _api.GetDevModsAsync();
            foreach (var m in mine)
                Mods.Add(new DevModItemViewModel(m, _api, this));
            HasNoMods = mine.Count == 0;
        }
        catch (Exception ex)
        {
            _setStatus($"Couldn't load your mods: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task BecomeDeveloperAsync()
    {
        try
        {
            var acct = await _api.BecomeDeveloperAsync();
            _session.UpdateAccount(acct);
            _setStatus("You're now a developer.");
        }
        catch (Exception ex) { _setStatus($"Failed: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task ApplyTrustedAsync()
    {
        try
        {
            var acct = await _api.ApplyTrustedAsync();
            _session.UpdateAccount(acct);
            _setStatus("Applied for trusted status.");
        }
        catch (Exception ex) { _setStatus($"Failed: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task UploadAsync()
    {
        UploadStatus = "";
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            UploadStatus = "Pick a .dll or .zip file first.";
            return;
        }

        IsBusy = true;
        try
        {
            var mod = await _api.UploadModAsync(
                FilePath, Name, Version, Author, Description, Dependencies, UploadKind, RepoUrl, ImagePaths);

            var tags = ParseTags(Tags);
            if (tags.Length > 0)
                await _api.SetTagsAsync(mod.Id, tags);

            UploadStatus = $"Uploaded {mod.Name} v{mod.Version}.";
            _setStatus(UploadStatus);
            ClearForm();
            await LoadMineAsync();
        }
        catch (ApiException ex) { UploadStatus = ex.Message; }
        catch (Exception ex) { UploadStatus = $"Upload failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    public void Remove(DevModItemViewModel item)
    {
        Mods.Remove(item);
        HasNoMods = Mods.Count == 0;
    }

    public void Notify(string message) => _setStatus(message);

    private void ClearForm()
    {
        FilePath = "";
        Name = "";
        Version = "1.0.0";
        Description = "";
        Dependencies = "";
        Tags = "";
    }

    public static string[] ParseTags(string raw) =>
        (raw ?? "")
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToArray();
}

public partial class DevModItemViewModel : ViewModelBase
{
    private readonly ApiClient _api;
    private readonly DevPanelViewModel _parent;

    public DevModItemViewModel(ModDto model, ApiClient api, DevPanelViewModel parent)
    {
        _api = api;
        _parent = parent;
        ModId = model.Id;
        Name = model.Name;
        Header = $"{model.Name}  |  v{model.Version}  |  {model.Status}  |  {model.Downloads} downloads";
        _editVersion = model.Version;
        _editDescription = model.Description;
        _editTags = string.Join(", ", model.Tags);
        _editRepoUrl = model.RepoUrl;
        _newVersion = model.Version;
    }

    public int ModId { get; }
    public string Name { get; }
    public string Header { get; }

    [ObservableProperty] private string _editVersion;
    [ObservableProperty] private string _editDescription;
    [ObservableProperty] private string _editTags;
    [ObservableProperty] private string _editRepoUrl;
    [ObservableProperty] private string _newVersion;
    [ObservableProperty] private string _newVersionFilePath = "";
    [ObservableProperty] private string _imageStatus = "";

    public List<string> PendingImages { get; } = new();
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            await _api.UpdateModAsync(ModId, EditVersion, EditDescription, null, EditRepoUrl);
            await _api.SetTagsAsync(ModId, DevPanelViewModel.ParseTags(EditTags));
            _parent.Notify($"Saved {Name}.");
        }
        catch (Exception ex) { _parent.Notify($"Save failed: {ex.Message}"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task UploadVersionAsync()
    {
        if (string.IsNullOrWhiteSpace(NewVersionFilePath))
        {
            _parent.Notify("Pick a .dll or .zip for the new version first.");
            return;
        }
        IsBusy = true;
        try
        {
            var mod = await _api.UploadVersionAsync(ModId, NewVersionFilePath, NewVersion);
            _parent.Notify($"Published {Name} v{mod.Version}.");
            NewVersionFilePath = "";
            await _parent.RefreshAsync();
        }
        catch (Exception ex) { _parent.Notify($"Version upload failed: {ex.Message}"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task AddImagesAsync()
    {
        if (PendingImages.Count == 0)
        {
            ImageStatus = "Pick one or more images first.";
            return;
        }
        IsBusy = true;
        try
        {
            await _api.AddImagesAsync(ModId, PendingImages);
            ImageStatus = $"Added {PendingImages.Count} image(s).";
            PendingImages.Clear();
            _parent.Notify("Images updated.");
        }
        catch (Exception ex) { ImageStatus = $"Upload failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ClearImagesAsync()
    {
        IsBusy = true;
        try
        {
            foreach (var imageId in await _api.GetImageIdsAsync(ModId))
                await _api.DeleteImageAsync(imageId);
            ImageStatus = "All images removed.";
            _parent.Notify("Images cleared.");
        }
        catch (Exception ex) { ImageStatus = $"Couldn't clear: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        IsBusy = true;
        try
        {
            await _api.DeleteModAsync(ModId);
            _parent.Notify($"Deleted {Name}.");
            _parent.Remove(this);
        }
        catch (Exception ex) { _parent.Notify($"Delete failed: {ex.Message}"); }
        finally { IsBusy = false; }
    }
}

public partial class PresetTagViewModel : ViewModelBase
{
    private readonly Action<string> _pick;

    public PresetTagViewModel(string name, Action<string> pick)
    {
        Name = name;
        _pick = pick;
    }

    public string Name { get; }

    [RelayCommand]
    private void Pick() => _pick(Name);
}
