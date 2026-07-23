using CommunityToolkit.Mvvm.Input;
using MikuMikuLoader.App.Models;
using MikuMikuLoader.App.Services;

namespace MikuMikuLoader.App.ViewModels;

public partial class ModItemViewModel : ViewModelBase
{
    private readonly InstallService _install;
    private readonly Action<string> _setStatus;
    private readonly Action<ModDto> _openDetail;
    private bool _busy;

    public ModItemViewModel(ModDto model, InstallService install, Action<string> setStatus, Action<ModDto> openDetail)
    {
        Model = model;
        _install = install;
        _setStatus = setStatus;
        _openDetail = openDetail;
    }

    public ModDto Model { get; }

    public string Name => Model.Name;
    public bool Trusted => Model.Trusted;
    public bool IsVerified => Model.Verified;
    public bool IsFeatured => Model.Featured;
    public bool IsReshade => string.Equals(Model.Kind, "Reshade", StringComparison.OrdinalIgnoreCase);
    public string[] Tags => Model.Tags;
    public bool HasTags => Model.Tags is { Length: > 0 };

    public string Description =>
        string.IsNullOrWhiteSpace(Model.Description) ? "No description provided." : Model.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Model.Description);

    public string Subtitle =>
        $"v{Model.Version}   |   by {Model.Author}   |   {SizeText}   |   {Model.Downloads} downloads";

    public string RatingText =>
        Model.RatingCount == 0 ? "Unrated" : $"Rated {Model.Rating:0.0} ({Model.RatingCount})";

    public bool IsInstalled => _install.IsInstalled(Model.Id);

    private bool NeedsUpdate =>
        IsInstalled && _install.GetInstalled(Model.Id)?.Version != Model.Version;

    public string InstallButtonText =>
        _busy ? "Working" : NeedsUpdate ? "Update" : IsInstalled ? "Installed" : "Install";

    public bool CanInstall => !_busy && (!IsInstalled || NeedsUpdate);

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (_install.GameFolder is null)
        {
            _setStatus("Set your Gorilla Tag folder in Settings before installing.");
            return;
        }
        if (!CanInstall) return;

        _busy = true;
        Refresh();
        _setStatus($"Installing {Model.Name}...");
        try
        {
            await _install.InstallAsync(Model);
            _setStatus($"Installed {Model.Name} v{Model.Version}.");
        }
        catch (Exception ex)
        {
            _setStatus($"Install failed: {ex.Message}");
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(InstallButtonText));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(IsInstalled));
    }

    [RelayCommand]
    private void Details() => _openDetail(Model);

    private string SizeText => FormatSize(Model.FileSizeBytes);

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "-";
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.#} {units[unit]}";
    }
}
