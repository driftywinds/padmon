using Microsoft.UI.Dispatching;
using Padmon.Core.History;
using Padmon.Core.Settings;
using Padmon.Input;

namespace Padmon.Services;

public sealed class AppServices
{
    public AppServices(DispatcherQueue dispatcher)
    {
        Dispatcher = dispatcher;
        SettingsStore = new JsonSettingsStore();
        History = new SqliteDeviceHistoryStore();
        Notifications = new NotificationService();

#if DEBUG
        if (Environment.GetEnvironmentVariable("PADMON_SIMULATE") == "1")
        {
            Input = new SimulatedControllerSource();
        }
        else
#endif
        {
            var sdl = new SdlControllerSource();
            var windowsGaming = new WindowsGamingControllerSource();
            Input = new CompositeControllerSource(sdl, windowsGaming);
        }

        Monitor = new ControllerMonitor(Input, History, dispatcher);
    }

    public DispatcherQueue Dispatcher { get; }

    public ISettingsStore SettingsStore { get; }

    public IDeviceHistoryStore History { get; }

    public NotificationService Notifications { get; }

    public IControllerSource Input { get; }

    public ControllerMonitor Monitor { get; }

    public PadmonSettings Settings { get; private set; } = new();

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Settings = await SettingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        await History.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await History.CloseOrphanedSessionsAsync(cancellationToken).ConfigureAwait(false);

        if (Settings.StartWithWindows != StartupService.IsEnabled())
        {
            StartupService.TrySetEnabled(Settings.StartWithWindows);
        }

        var retentionDays = Math.Clamp(Settings.HistoryRetentionDays, 1, 3650);
        await History.PruneSessionsAsync(
            DateTimeOffset.UtcNow.AddDays(-retentionDays),
            cancellationToken).ConfigureAwait(false);

        await Monitor.StartAsync(cancellationToken).ConfigureAwait(false);
    }
}
