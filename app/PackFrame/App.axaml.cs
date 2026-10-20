using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace PackFrame;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var config = AppConfig.Load();

            if (Localization.IsSupported(config.Language))
            {
                desktop.MainWindow = new MainWindow(config);
            }
            else
            {
                var languageWindow = new LanguageWindow();
                languageWindow.LanguageSelected += (_, language) =>
                {
                    config.Language = language;
                    config.Save();

                    var mainWindow = new MainWindow(config);
                    desktop.MainWindow = mainWindow;
                    mainWindow.Show();
                    languageWindow.Close();
                };
                desktop.MainWindow = languageWindow;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
