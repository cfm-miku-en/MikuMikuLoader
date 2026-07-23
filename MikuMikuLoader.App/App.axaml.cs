using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MikuMikuLoader.App.Services;
using MikuMikuLoader.App.ViewModels;
using MikuMikuLoader.App.Views;

namespace MikuMikuLoader.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settingsService = new SettingsService();
            var settings = settingsService.Load();

            Window BuildMain()
            {
                var api = new ApiClient();
                api.SetBaseUrl(settings.ServerUrl);

                var locator = new GameLocatorService();
                var installed = new InstalledModsService();
                var install = new InstallService(api, locator, installed, settingsService, settings);
                var session = new SessionService(api, settingsService, settings);

                var mainViewModel = new MainWindowViewModel(api, settingsService, settings, install, session);
                return new MainWindow { DataContext = mainViewModel };
            }

            if (settings.AcceptedTosVersion != TosInfo.Version)
            {
                var tos = new TosWindow();
                tos.Agreed += () =>
                {
                    settings.AcceptedTosVersion = TosInfo.Version;
                    settingsService.Save(settings);
                    var main = BuildMain();
                    desktop.MainWindow = main;
                    main.Show();
                    tos.Close();
                };
                tos.Declined += () => desktop.Shutdown();
                desktop.MainWindow = tos;
            }
            else
            {
                desktop.MainWindow = BuildMain();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
