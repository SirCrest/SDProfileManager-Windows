using Microsoft.UI.Xaml;
using SDProfileManager.Helpers;
using SDProfileManager.Services;

namespace SDProfileManager;

public partial class App : Application
{
    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppLog.Bootstrap();
        _mainWindow = new MainWindow();
        WindowHelper.SetWindow(_mainWindow);
        _mainWindow.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppLog.Critical($"Unhandled exception: {e.Exception}");

        if (e.Exception is LayoutCycleException)
        {
            e.Handled = true;
            AppLog.Error("Recovered from layout cycle by resetting pane split.");
            if (_mainWindow is MainWindow window)
                window.RecoverFromLayoutCycle();
        }
    }

    private Window? _mainWindow;
}
