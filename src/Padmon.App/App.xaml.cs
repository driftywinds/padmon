using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Padmon.Core;
using Padmon.Core.Diagnostics;
using Padmon.Services;

namespace Padmon;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    public static AppServices Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Must run before PadmonPaths is first touched. Packaged builds keep
        // settings, history, and logs in the package's local data folder.
        AppEnvironment.ConfigureDataDirectory();
        PadmonLog.Initialize(Path.Combine(PadmonPaths.LogsDirectory, "padmon.log"));
        var arguments = Environment.GetCommandLineArgs();
        PadmonLog.Info($"launched: {string.Join(' ', arguments)}");

        if (ElevatedToastRelay.IsToastCommand(arguments))
        {
            Environment.Exit(ElevatedToastRelay.HandleToastCommand(arguments));
            return;
        }

        // Must happen before the first window exists so tray balloons are
        // attributed to padmon's registered identity, elevated or not.
        DesktopToastService.EnsureIdentity();

        Services = new AppServices(DispatcherQueue.GetForCurrentThread());

        var startHidden = Environment.GetCommandLineArgs()
            .Any(argument => string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));

        MainWindow = new MainWindow();
        MainWindow.Activate();

        if (startHidden && MainWindow is MainWindow window)
        {
            MainWindow.DispatcherQueue.TryEnqueue(window.HideToTray);
        }

        _ = StartMonitoringAsync();
    }

    private static async Task StartMonitoringAsync()
    {
        try
        {
            Services.Monitor.ControllerConnected += OnControllerConnected;
            Services.Monitor.ControllerDisconnected += OnControllerDisconnected;
            await Services.StartAsync();

            if (MainWindow is MainWindow window)
            {
                window.ApplyTheme(Services.Settings.Theme);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"padmon input failed to start: {exception}");
            PadmonLog.Error("input failed to start", exception);
            LogStartupFailure(exception);
        }
    }

    private static void OnControllerConnected(object? sender, ViewModels.ControllerViewModel controller)
    {
        PadmonLog.Info($"connect notification requested: name='{controller.DisplayName}'");

        if (Services.Settings.NotifyOnConnect && ShouldNotify())
        {
            Services.Notifications.ShowConnected(controller.DisplayName, controller.Device);
        }
    }

    private static void OnControllerDisconnected(object? sender, ViewModels.ControllerViewModel controller)
    {
        PadmonLog.Info($"disconnect notification requested: name='{controller.DisplayName}'");

        if (Services.Settings.NotifyOnDisconnect && ShouldNotify())
        {
            Services.Notifications.ShowDisconnected(controller.DisplayName, controller.Device);
        }
    }

    private static bool ShouldNotify()
    {
        return !Services.Settings.NotifyOnlyWhenBackground
            || App.MainWindow is not MainWindow { IsWindowActive: true };
    }

    private static void LogStartupFailure(Exception exception)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "padmon-startup-error.log");
            File.AppendAllText(
                path,
                $"[{DateTimeOffset.Now:O}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}