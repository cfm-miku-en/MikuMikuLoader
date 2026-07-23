using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuMikuLoader.App.Services;

namespace MikuMikuLoader.App.ViewModels;

public partial class LibraryViewModel : ViewModelBase
{
    private readonly InstallService _install;
    private readonly Action<string> _setStatus;

    public LibraryViewModel(InstallService install, Action<string> setStatus)
    {
        _install = install;
        _setStatus = setStatus;
        Refresh();
    }

    public ObservableCollection<LibraryItemViewModel> Items { get; } = new();

    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _gameFolderStatus = "";
    [ObservableProperty] private bool _hasGameFolder;

    public void Refresh()
    {
        Items.Clear();
        foreach (var p in _install.ScanPlugins())
            Items.Add(LibraryItemViewModel.ForPlugin(p, this));
        foreach (var r in _install.InstalledReshades)
            Items.Add(LibraryItemViewModel.ForReshade(r, this));

        IsEmpty = Items.Count == 0;
        HasGameFolder = _install.GameFolder is not null;
        GameFolderStatus = _install.GameFolder is null
            ? "Gorilla Tag folder not set - auto-detect it or set it in Settings."
            : $"Plugins folder: {_install.GameFolder}\\BepInEx\\plugins";
    }

    [RelayCommand]
    private void DetectGame()
    {
        var found = _install.Detect();
        _setStatus(found is null
            ? "Couldn't find Gorilla Tag automatically. Set it in Settings."
            : $"Found Gorilla Tag at {found}.");
        Refresh();
    }

    [RelayCommand]
    private void OpenPlugins() => _install.OpenPluginsFolder();

    [RelayCommand]
    private void LaunchGame()
    {
        _install.LaunchGame();
        _setStatus("Launching Gorilla Tag through Steam...");
    }

    [RelayCommand]
    private void Refreshing() => Refresh();

    public void TogglePlugin(PluginFile plugin)
    {
        _install.SetPluginEnabled(plugin, !plugin.Enabled);
        _setStatus(plugin.Enabled ? $"Disabled {plugin.Name}." : $"Enabled {plugin.Name}.");
        Refresh();
    }

    public void DeletePlugin(PluginFile plugin)
    {
        _install.DeletePlugin(plugin);
        _setStatus($"Deleted {plugin.FileName}.");
        Refresh();
    }

    public void UninstallReshade(InstalledMod reshade)
    {
        _install.UninstallReshade(reshade);
        _setStatus($"Removed reshade {reshade.Name}.");
        Refresh();
    }
}

public partial class LibraryItemViewModel : ViewModelBase
{
    private readonly LibraryViewModel _parent;
    private readonly PluginFile? _plugin;
    private readonly InstalledMod? _reshade;

    private LibraryItemViewModel(LibraryViewModel parent, PluginFile? plugin, InstalledMod? reshade)
    {
        _parent = parent;
        _plugin = plugin;
        _reshade = reshade;
    }

    public static LibraryItemViewModel ForPlugin(PluginFile plugin, LibraryViewModel parent) =>
        new(parent, plugin, null);

    public static LibraryItemViewModel ForReshade(InstalledMod reshade, LibraryViewModel parent) =>
        new(parent, null, reshade);

    public bool CanToggle => _plugin is not null;
    public bool Enabled => _plugin?.Enabled ?? true;
    public string ToggleText => (_plugin?.Enabled ?? true) ? "Disable" : "Enable";

    public string Title => _reshade is not null
        ? _reshade.Name
        : (_plugin!.IsTracked ? _plugin.Name : _plugin.FileName);

    public string Detail => _reshade is not null
        ? $"Reshade  |  v{_reshade.Version}  |  {(_reshade.Files?.Count ?? 0)} files in game root"
        : (_plugin!.IsTracked ? $"v{_plugin.Version}  |  {_plugin.FileName}" : "External DLL - not installed through this loader");

    [RelayCommand]
    private void Toggle()
    {
        if (_plugin is not null) _parent.TogglePlugin(_plugin);
    }

    [RelayCommand]
    private void Delete()
    {
        if (_reshade is not null) _parent.UninstallReshade(_reshade);
        else if (_plugin is not null) _parent.DeletePlugin(_plugin);
    }
}
