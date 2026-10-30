using System.Runtime.InteropServices;
using System.Text;
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
            Log($"started args={args.Length}");

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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
