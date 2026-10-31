using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Padmon.Core.Diagnostics;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Padmon.Services;

/// <summary>
/// Native Windows toast path for an unpackaged desktop app. Windows only shows
/// toasts for an AppUserModelID it knows about, so this registers the AUMID in
/// HKCU (the reliable path, no timing dependency) and also stamps it onto a
/// Start Menu shortcut. Toasts are always sent through an explicit-AUMID
/// notifier so they do not depend on whichever AUMID the process ended up with.
/// </summary>
internal static class DesktopToastService
{
    private const string Aumid = "Padmon.ControllerMonitor";
    private const string DisplayName = "padmon";
    private const ushort VtLpwstr = 31;

    private static readonly Guid GuidAppUserModel = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private static readonly string ShortcutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft",
        "Windows",
        "Start Menu",
        "Programs",
        "padmon.lnk");

    private static readonly object Sync = new();
    private static bool _prepared;
    private static bool _identityReady;
    private static ToastNotification? _lastToast; // keep alive so events still fire

    /// <summary>
    /// Gives this process a stable, registered notification identity. Windows
    /// converts tray balloons (Shell_NotifyIcon) into toasts and attributes them
    /// to the process AppUserModelID; without a registered one the balloon is
    /// attributed to a path-derived identity that differs by launch method and
    /// elevation, and can be silently suppressed. Must run before any window is
    /// created. Does not touch the Start Menu shortcut.
    /// </summary>
    public static void EnsureIdentity()
    {
        lock (Sync)
        {
            if (_identityReady)
            {
                return;
            }

            _identityReady = true;

            var result = SetCurrentProcessExplicitAppUserModelID(Aumid);
            PadmonLog.Info($"notification identity: AUMID set result=0x{result:X8}");
            RegisterAumid();

            try
            {
                var setting = ToastNotificationManager.CreateToastNotifier(Aumid).Setting;
                PadmonLog.Info($"notification identity: Windows notification setting={setting}");

                if (setting != NotificationSetting.Enabled)
                {
                    PadmonLog.Warn(
                        $"notifications for padmon are blocked by Windows ({setting}); check Settings > System > Notifications and Focus/Do Not Disturb");
                }
            }
            catch (Exception exception)
            {
                PadmonLog.Warn($"notification identity: could not read notification setting: {exception.Message}");
            }
        }
    }

    public static void Initialize()
    {
        var result = SetCurrentProcessExplicitAppUserModelID(Aumid);
        PadmonLog.Info($"native desktop toast AUMID initialized: result=0x{result:X8}");
        Prepare();
    }

    /// <summary>Registers the AUMID and shortcut once per process.</summary>
    public static bool Prepare()
    {
        lock (Sync)
        {
            if (_prepared)
            {
                return true;
            }

            var registered = RegisterAumid();
            var shortcut = EnsureShortcut();
            _prepared = registered || shortcut;
            return _prepared;
        }
    }

    public static bool TryShow(string title, string body)
    {
        try
        {
            if (!Prepare())
            {
                return false;
            }

            var xml = new XmlDocument();
            xml.LoadXml($"""
<toast activationType="foreground">
  <visual>
    <binding template="ToastGeneric">
      <text>{Escape(title)}</text>
      <text>{Escape(body)}</text>
    </binding>
  </visual>
  <audio silent="false" />
</toast>
""");

            var notifier = ToastNotificationManager.CreateToastNotifier(Aumid);

            try
            {
                var setting = notifier.Setting;
                PadmonLog.Info($"native desktop toast notifier setting={setting}");

                if (setting != NotificationSetting.Enabled)
                {
                    PadmonLog.Warn(
                        $"native desktop toast blocked by Windows ({setting}); check Settings > System > Notifications, Focus/Do Not Disturb, and group policy");
                    return false;
                }
            }
            catch (Exception exception)
            {
                PadmonLog.Warn(
                    $"could not read native toast notifier setting; attempting display anyway: {exception.Message}");
            }

            var toast = new ToastNotification(xml)
            {
                Tag = "padmon-controller",
                Group = "padmon"
            };

            toast.Activated += (_, _) => BringToFront();
            toast.Failed += (_, args) =>
                PadmonLog.Error($"native desktop toast failed to display: 0x{args.ErrorCode?.HResult ?? 0:X8}", args.ErrorCode);

            _lastToast = toast;
            notifier.Show(toast);

            PadmonLog.Info("native desktop toast shown");
            return true;
        }
        catch (Exception exception)
        {
            PadmonLog.Error("native desktop toast failed", exception);
            return false;
        }
    }

    private static void BringToFront()
    {
        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            App.MainWindow.AppWindow.Show();
            App.MainWindow.Activate();
        });
    }

    private static bool RegisterAumid()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{Aumid}");
            key.SetValue("DisplayName", DisplayName, RegistryValueKind.String);

            var icon = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "padmon.png"),
                Path.Combine(AppContext.BaseDirectory, "Assets", "padmon-tray.ico")
            }.FirstOrDefault(File.Exists);

            if (icon is not null)
            {
                key.SetValue("IconUri", icon, RegistryValueKind.String);
            }

            PadmonLog.Info($"native desktop toast AUMID registered in HKCU (icon={icon ?? "none"})");
            return true;
        }
        catch (Exception exception)
        {
            PadmonLog.Error("native desktop toast AUMID registry registration failed", exception);
            return false;
        }
    }

    private static bool EnsureShortcut()
    {
        var executable = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName;

        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            PadmonLog.Info("native desktop toast: executable path unavailable");
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath)!);

            var link = (IShellLinkW)new ShellLink();
            var persist = (IPersistFile)link;
            var store = (IPropertyStore)link;
            var exists = File.Exists(ShortcutPath);

            if (exists)
            {
                // Keep a shortcut the user already made (target, arguments);
                // only stamp the AUMID onto it.
                persist.Load(ShortcutPath, 0);

                if (ReadAumid(store) == Aumid)
                {
                    PadmonLog.Info($"native desktop toast shortcut already has AUMID: {ShortcutPath}");
                    return true;
                }

                var target = new StringBuilder(520);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);

                if (!File.Exists(target.ToString()))
                {
                    link.SetPath(executable);
                    link.SetWorkingDirectory(Path.GetDirectoryName(executable)!);
                }
            }
            else
            {
                link.SetPath(executable);
                link.SetWorkingDirectory(Path.GetDirectoryName(executable)!);
                link.SetArguments("");
                link.SetDescription("padmon controller monitor");
                link.SetIconLocation(executable, 0);
            }

            WriteAumid(store, Aumid);
            store.Commit();
            persist.Save(ShortcutPath, true);
            SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);
            PadmonLog.Info($"native desktop toast shortcut installed: {ShortcutPath}");
            return true;
        }
        catch (Exception exception)
        {
            PadmonLog.Error("native desktop toast shortcut install failed", exception);
            return File.Exists(ShortcutPath);
        }
    }

    private static string? ReadAumid(IPropertyStore store)
    {
        var key = new PropertyKey(GuidAppUserModel, 5);
        store.GetValue(ref key, out var value);

        try
        {
            return value.vt == VtLpwstr && value.pointerValue != IntPtr.Zero
                ? Marshal.PtrToStringUni(value.pointerValue)
                : null;
        }
        finally
        {
            _ = PropVariantClear(ref value);
        }
    }

    private static void WriteAumid(IPropertyStore store, string appUserModelId)
    {
        var key = new PropertyKey(GuidAppUserModel, 5);
        var value = new PropVariant
        {
            vt = VtLpwstr,
            pointerValue = Marshal.StringToCoTaskMemUni(appUserModelId)
        };

        try
        {
            store.SetValue(ref key, ref value);
        }
        finally
        {
            _ = PropVariantClear(ref value);
        }
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private const int ShcneAssocChanged = 0x08000000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, ref PropVariant pv);
        void Commit();
    }

    [ComImport]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig]
        int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;

        public PropertyKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)] // PROPVARIANT is 24 bytes on x64
    private struct PropVariant
    {
        [FieldOffset(0)]
        public ushort vt;

        [FieldOffset(8)]
        public IntPtr pointerValue;
    }
}