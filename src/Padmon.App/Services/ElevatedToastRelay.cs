using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Padmon.Core.Diagnostics;

namespace Padmon.Services;

/// <summary>
/// Windows does not display app notifications from elevated processes. When
/// padmon is running as administrator, relay the toast to a short-lived copy of
/// padmon launched with the normal Explorer token instead.
/// </summary>
internal static class ElevatedToastRelay
{
    private const string ToastSwitch = "--toast";
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenAssignPrimary = 0x0001;
    private const uint TokenDuplicate = 0x0002;
    private const uint TokenQuery = 0x0008;
    private const uint MaximumAllowed = 0x02000000;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;

    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryShow(string title, string body)
    {
        if (!IsElevated())
        {
            return false;
        }

        var executable = Path.Combine(AppContext.BaseDirectory, "toast", "Padmon.Toast.exe");
        if (!File.Exists(executable))
        {
            PadmonLog.Info($"elevated toast relay: helper missing at {executable}");
            return false;
        }

        var titleArgument = Convert.ToBase64String(Encoding.UTF8.GetBytes(title));
        var bodyArgument = Convert.ToBase64String(Encoding.UTF8.GetBytes(body));
        var commandLine = $"\"{executable}\" {ToastSwitch} {titleArgument} {bodyArgument}";
        var explorer = FindExplorer();

        if (explorer is null)
        {
            PadmonLog.Info("elevated toast relay: no Explorer process found");
            return false;
        }

        IntPtr processHandle = IntPtr.Zero;
        IntPtr token = IntPtr.Zero;
        IntPtr primaryToken = IntPtr.Zero;
        PROCESS_INFORMATION processInformation = default;

        try
        {
            processHandle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)explorer.Id);
            if (processHandle == IntPtr.Zero)
            {
                return false;
            }

            if (!OpenProcessToken(
                    processHandle,
                    TokenAssignPrimary | TokenDuplicate | TokenQuery,
                    out token))
            {
                return false;
            }

            if (!DuplicateTokenEx(
                    token,
                    MaximumAllowed,
                    IntPtr.Zero,
                    SecurityImpersonation,
                    TokenPrimary,
                    out primaryToken))
            {
                return false;
            }

            var startup = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = "winsta0\\default"
            };

            if (!CreateProcessWithTokenW(
                    primaryToken,
                    0,
                    null,
                    commandLine,
                    0,
                    IntPtr.Zero,
                    Path.GetDirectoryName(executable),
                    ref startup,
                    out processInformation))
            {
                PadmonLog.Error(
                    $"elevated toast relay: CreateProcessWithTokenW failed: {Marshal.GetLastWin32Error()}");
                return false;
            }

            PadmonLog.Info("elevated toast relay: helper started");

            var wait = WaitForSingleObject(processInformation.hProcess, 3000);
            if (wait == WaitTimeout)
            {
                PadmonLog.Info("elevated toast relay: helper still running; assuming delivered");
                return true;
            }

            if (!GetExitCodeProcess(processInformation.hProcess, out var exitCode))
            {
                return true;
            }

            PadmonLog.Info($"elevated toast relay: helper exit={exitCode}");
            return exitCode == 0;
        }
        catch (Exception exception)
        {
            PadmonLog.Error("elevated toast relay failed", exception);
            return false;
        }
        finally
        {
            if (processInformation.hThread != IntPtr.Zero)
            {
                CloseHandle(processInformation.hThread);
            }

            if (processInformation.hProcess != IntPtr.Zero)
            {
                CloseHandle(processInformation.hProcess);
            }

            if (primaryToken != IntPtr.Zero)
            {
                CloseHandle(primaryToken);
            }

            if (token != IntPtr.Zero)
            {
                CloseHandle(token);
            }

            if (processHandle != IntPtr.Zero)
            {
                CloseHandle(processHandle);
            }
        }
    }

    public static bool IsToastCommand(string[] arguments) =>
        arguments.Length >= 4 &&
        string.Equals(arguments[1], ToastSwitch, StringComparison.OrdinalIgnoreCase);

    public static int HandleToastCommand(string[] arguments)
    {
        try
        {
            var title = Encoding.UTF8.GetString(Convert.FromBase64String(arguments[2]));
            var body = Encoding.UTF8.GetString(Convert.FromBase64String(arguments[3]));

            DesktopToastService.Initialize();
            var shown = DesktopToastService.TryShow(title, body);

            // Let the shell hand the toast to the notification platform before
            // this short-lived process disappears.
            Thread.Sleep(750);
            return shown ? 0 : 1;
        }
        catch (Exception exception)
        {
            PadmonLog.Error("toast helper failed", exception);
            return 1;
        }
    }

    private static Process? FindExplorer()
    {
        var session = Process.GetCurrentProcess().SessionId;
        var candidates = Process.GetProcessesByName("explorer")
            .Where(process => process.SessionId == session)
            .Select(process => new
            {
                Process = process,
                Integrity = GetIntegrityRid(process.Id)
            })
            .OrderByDescending(candidate => candidate.Integrity == 0x2000 ? 2 : candidate.Integrity == 0x1000 ? 0 : 1)
            .ToArray();

        var selected = candidates.FirstOrDefault();

        if (selected is not null)
        {
            PadmonLog.Info(
                $"elevated toast relay: shell pid={selected.Process.Id} integrity=0x{selected.Integrity:X}");
        }

        return selected?.Process;
    }

    private static int GetIntegrityRid(int processId)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
        if (process == IntPtr.Zero)
        {
            return -1;
        }

        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token))
            {
                return -2;
            }

            try
            {
                GetTokenInformation(token, 25, IntPtr.Zero, 0, out var length);
                var buffer = Marshal.AllocHGlobal(length);

                try
                {
                    if (!GetTokenInformation(token, 25, buffer, length, out _))
                    {
                        return -3;
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
        finally
        {
            CloseHandle(process);
        }
    }

    private const uint WaitTimeout = 0x00000102;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

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

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr existingToken,
        uint desiredAccess,
        IntPtr tokenAttributes,
        int impersonationLevel,
        int tokenType,
        out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(
        IntPtr token,
        uint logonFlags,
        string? applicationName,
        string commandLine,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref STARTUPINFO startupInfo,
        out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
