using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MikuMikuLoader.App.ViewModels;

namespace MikuMikuLoader.App.Views;

public partial class DevPanelView : UserControl
{
    public DevPanelView()
    {
        InitializeComponent();
    }

    private async void OnPickFile(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || DataContext is not DevPanelViewModel vm) return;

        var isReshade = string.Equals(vm.UploadKind, "Reshade", StringComparison.OrdinalIgnoreCase);

        var fileType = isReshade
            ? new FilePickerFileType("Reshade files") { Patterns = new[] { "*.zip", "*.ini" } }
            : new FilePickerFileType("Mod files") { Patterns = new[] { "*.dll", "*.zip" } };

        var result = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = isReshade
                ? "Select your reshade (.zip or .ini)"
                : "Select your mod (.dll or .zip)",
            AllowMultiple = false,
            FileTypeFilter = new[] { fileType }
        });

        if (result.Count > 0)
        {
            var path = result[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
                vm.FilePath = path;
        }
    }

    private async void OnPickImage(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || DataContext is not DevPanelViewModel vm) return;

        var imageType = new FilePickerFileType("Images")
        {
            Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" }
        };

        var result = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select preview images",
            AllowMultiple = true,
            FileTypeFilter = new[] { imageType }
        });

        vm.ImagePaths.Clear();
        foreach (var f in result)
        {
            var path = f.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) vm.ImagePaths.Add(path);
        }
        vm.ImagePath = vm.ImagePaths.Count switch
        {
            0 => "",
            1 => vm.ImagePaths[0],
            _ => $"{vm.ImagePaths.Count} images selected"
        };
    }

    private void OnClearImage(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevPanelViewModel vm) return;
        vm.ImagePath = "";
        vm.ImagePaths.Clear();
    }

    private async void OnPickModImages(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || sender is not Control control || control.DataContext is not DevModItemViewModel item) return;

        var imageType = new FilePickerFileType("Images")
        {
            Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" }
        };

        var result = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select images",
            AllowMultiple = true,
            FileTypeFilter = new[] { imageType }
        });

        item.PendingImages.Clear();
        foreach (var f in result)
        {
            var path = f.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) item.PendingImages.Add(path);
        }
        item.ImageStatus = item.PendingImages.Count == 0
            ? "No images selected."
            : $"{item.PendingImages.Count} image(s) ready - hit Upload.";
    }

    private async void OnPickVersionFile(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || sender is not Control control || control.DataContext is not DevModItemViewModel item) return;

        var modType = new FilePickerFileType("Mod files")
        {
            Patterns = new[] { "*.dll", "*.zip" }
        };

        var result = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select the new version (.dll or .zip)",
            AllowMultiple = false,
            FileTypeFilter = new[] { modType }
        });

        if (result.Count > 0)
        {
            var path = result[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
                item.NewVersionFilePath = path;
        }
    }
}
