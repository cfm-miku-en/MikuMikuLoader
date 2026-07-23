using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MikuMikuLoader.App.Services;

namespace MikuMikuLoader.App.Views;

public partial class TosWindow : Window
{
    public event Action? Agreed;
    public event Action? Declined;

    public TosWindow()
    {
        InitializeComponent();
        TosText.Text = TosInfo.Text;
    }

    private void OnAgree(object? sender, RoutedEventArgs e) => Agreed?.Invoke();

    private void OnDecline(object? sender, RoutedEventArgs e) => Declined?.Invoke();
}
