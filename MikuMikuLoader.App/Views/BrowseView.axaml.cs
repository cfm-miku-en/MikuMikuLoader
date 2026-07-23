using Avalonia.Controls;
using Avalonia.Input;
using MikuMikuLoader.App.ViewModels;

namespace MikuMikuLoader.App.Views;

public partial class BrowseView : UserControl
{
    public BrowseView()
    {
        InitializeComponent();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is BrowseViewModel vm)
            vm.SearchCommand.Execute(null);
    }
}
