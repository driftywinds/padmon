using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

internal static class Program
{
    private const string Aumid = "Padmon.ControllerMonitor";

    [STAThread]
    private static int Main(string[] args)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "padmon-toast-helper.log");

        void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(
                    logPath,
                    $"{DateTimeOffset.Now:O} [pid={Environment.ProcessId}] {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }

        try
        {
            Log($"started args={args.Length} integrity=0x{GetIntegrityRid():X}");

            if (args.Length < 2 || !string.Equals(args[0], "--toast", StringComparison.OrdinalIgnoreCase))
            {
                Log("missing --toast arguments");
                return 2;
            }

            var title = Encoding.UTF8.GetString(Convert.FromBase64String(args[1]));
            var body = args.Length > 2
                ? Encoding.UTF8.GetString(Convert.FromBase64String(args[2]))
                : string.Empty;

            var aumidResult = SetCurrentProcessExplicitAppUserModelID(Aumid);
            Log($"AUMID result=0x{aumidResult:X8}");

            try
            {
                AppNotificationManager.Default.Register();
                var appNotification = new AppNotificationBuilder()
                    .AddText(title)
                    .AddText(body)
                    .BuildNotification();

                AppNotificationManager.Default.Show(appNotification);
                Log("App SDK toast show returned");
                Thread.Sleep(750);
                return 0;
            }
            catch (Exception exception)
            {
                Log($"App SDK toast failed; trying legacy AUMID path: {exception}");
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
                Log($"notifier setting={notifier.Setting}");
            }
            catch (Exception exception)
            {
                Log($"notifier setting failed: {exception.Message}");
            }

            var toast = new ToastNotification(xml)
            {
                Tag = "padmon-controller",
                Group = "padmon"
            };

            toast.Failed += (_, eventArgs) =>
                Log($"toast failed: 0x{eventArgs.ErrorCode?.HResult ?? 0:X8}");

            notifier.Show(toast);
            Log("toast show returned");
            Thread.Sleep(750);
            return 0;
        }
        catch (Exception exception)
        {
            Log($"failed: {exception}");
            return 1;
        }
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static int GetIntegrityRid()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out var token))
        {
            return -1;
        }

        try
        {
            GetTokenInformation(token, 25, IntPtr.Zero, 0, out var length);
            var buffer = Marshal.AllocHGlobal(length);

            try
            {
                if (!GetTokenInformation(token, 25, buffer, length, out _))
                {
                    return -2;
                }

                var sid = Marshal.ReadIntPtr(buffer, 0);
                var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                var rid = GetSidSubAuthority(sid, (uint)(count - 1));
                return Marshal.ReadInt32(rid);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr token,
        int infoClass,
        IntPtr info,
        int length,
        out int returnLength);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
