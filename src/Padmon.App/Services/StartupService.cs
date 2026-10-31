using Microsoft.Win32;
using Windows.ApplicationModel;

namespace Padmon.Services;

public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "padmon";
    private const string PackagedTaskId = "PadmonStartup";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public static async Task<bool> IsEnabledAsync()
    {
        if (AppEnvironment.IsPackaged)
        {
            try
            {
                var task = await StartupTask.GetAsync(PackagedTaskId);
                return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }
            catch
            {
                return false;
            }
        }

        return IsEnabled();
    }

    public static async Task<bool> TrySetEnabledAsync(bool enabled)
    {
        if (AppEnvironment.IsPackaged)
        {
            try
            {
                var task = await StartupTask.GetAsync(PackagedTaskId);

                if (enabled)
                {
                    if (task.State is StartupTaskState.DisabledByUser or StartupTaskState.DisabledByPolicy)
                    {
                        return false;
                    }

                    var state = await task.RequestEnableAsync();
                    return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
                }

                task.Disable();
                return true;
            }
            catch
            {
                return false;
            }
        }

        return TrySetEnabled(enabled);
    }

    public static bool TrySetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (enabled)
            {
                // The portable launcher passes its own path so autostart survives updates.
                var executable = Environment.GetEnvironmentVariable("PADMON_PORTABLE_EXE");
                if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                {
                    executable = Environment.ProcessPath;
                }

                if (string.IsNullOrWhiteSpace(executable))
                {
                    return false;
                }

                key.SetValue(ValueName, $"\"{executable}\" --background");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
