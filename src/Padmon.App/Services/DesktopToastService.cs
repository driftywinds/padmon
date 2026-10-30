using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Padmon.Core.Diagnostics;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Padmon.Services;

/// <summary>
/// Native Windows toast path for an unpackaged desktop app. Windows requires a
/// Start Menu shortcut carrying an AppUserModelID before it will accept an
/// explicit-AUMID toast. This is the documented desktop-app path; the Windows
/// App SDK AppNotificationManager is tried first.
/// </summary>
internal static class DesktopToastService
{
    private const string Aumid = "Padmon.ControllerMonitor";
    private static readonly Guid GuidAppUserModel = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private static readonly string ShortcutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft",
        "Windows",
        "Start Menu",
        "Programs",
        "padmon.lnk");

    public static void Initialize()
    {
        var result = SetCurrentProcessExplicitAppUserModelID(Aumid);
        PadmonLog.Info($"native desktop toast AUMID initialized: result={result}");
    }

    public static bool TryShow(string title, string body)
    {
        try
        {
            if (!EnsureShortcut())
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

            var processAumidResult = SetCurrentProcessExplicitAppUserModelID(Aumid);
            PadmonLog.Info($"native desktop toast process AUMID result={processAumidResult}");

            ToastNotifier notifier;

            try
            {
                notifier = ToastNotificationManager.CreateToastNotifier();
            }
            catch
            {
                notifier = ToastNotificationManager.CreateToastNotifier(Aumid);
            }

            notifier.Show(new ToastNotification(xml)
            {
                Tag = "padmon-controller",
                Group = "padmon"
            });

            PadmonLog.Info("native desktop toast shown");
            return true;
        }
        catch (Exception exception)
        {
            PadmonLog.Error("native desktop toast failed", exception);
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

        var temporaryShortcut = Path.Combine(Path.GetTempPath(), "padmon-aumid.lnk");

        try
        {
            CreateShortcut(temporaryShortcut, executable, Aumid);
            Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath)!);
            File.Copy(temporaryShortcut, ShortcutPath, true);
            SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);
            PadmonLog.Info($"native desktop toast shortcut installed: {ShortcutPath}");
            return true;
        }
        catch (Exception exception)
        {
            PadmonLog.Error("native desktop toast shortcut install failed", exception);

            if (File.Exists(ShortcutPath))
            {
                PadmonLog.Info($"using existing native desktop toast shortcut: {ShortcutPath}");
                return true;
            }

            return false;
        }
        finally
        {
            try
            {
                File.Delete(temporaryShortcut);
            }
            catch
            {
            }
        }
    }

    private static void CreateShortcut(string path, string target, string appUserModelId)
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(target);
        link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
        link.SetArguments("");
        link.SetDescription("padmon controller monitor");
        link.SetIconLocation(target, 0);

        var store = (IPropertyStore)link;
        var key = new PropertyKey(GuidAppUserModel, 5);
        var value = new PropVariant
        {
            vt = 31,
            pointerValue = Marshal.StringToCoTaskMemUni(appUserModelId)
        };

        try
        {
            store.SetValue(ref key, ref value);
            store.Commit();
        }
        finally
        {
            _ = PropVariantClear(ref value);
        }

        ((IPersistFile)link).Save(path, true);
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

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        public ushort vt;

        [FieldOffset(8)]
        public IntPtr pointerValue;
    }
}
