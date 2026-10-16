using System.Runtime.InteropServices;
using System.Text;

namespace CodexQuotaMonitor.Wpf;

public static class NativeMethods
{
    public const int ABM_GETTASKBARPOS = 5;
    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;
    public const int GWLP_HWNDPARENT = -8;
    public const uint GW_OWNER = 4;
    public const int GWL_EXSTYLE = -20;
    public const int GA_ROOT = 2;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int ERROR_ALREADY_EXISTS = 183;

    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOOWNERZORDER = 0x0200;

    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_APPWINDOW = 0x00040000;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public nint lParam;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    public static extern nuint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern nint GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    public static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern nint SetWindowLongPtr64(IntPtr hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    public static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateMutexW(IntPtr lpMutexAttributes, bool bInitialOwner, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    public static extern uint GetLastError();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AttachConsole(uint dwProcessId);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int virtualKey);

    public const int VK_LBUTTON = 0x01;
    public const int VK_RBUTTON = 0x02;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    // Any mouse button down anywhere on screen. The overlay window is WS_EX_NOACTIVATE and never
    // activates, so a click in another application never shows up as a WPF input event: this is
    // how the panel finds out the mouse went down outside it.
    public static bool MouseButtonDown()
    {
        return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0 || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
    }

    public static IntPtr RootWindow(IntPtr hwnd)
    {
        var root = GetAncestor(hwnd, GA_ROOT);
        return root == IntPtr.Zero ? hwnd : root;
    }

    public static nint GetWindowLongPtr(IntPtr hwnd, int index)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : GetWindowLong32(hwnd, index);
    }

    public static bool TryGetTaskbarRect(out uint edge, out RECT rect)
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        var result = SHAppBarMessage(ABM_GETTASKBARPOS, ref data);
        edge = data.uEdge;
        rect = data.rc;
        if (result != 0 && rect.Width > 0 && rect.Height > 0)
        {
            return true;
        }

        // ABM_GETTASKBARPOS round-trips through the shell and can fail (shell restarting,
        // restricted or elevated process). The taskbar window itself still has geometry.
        var taskbar = FindWindowW("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            edge = 0;
            rect = default;
            return false;
        }

        edge = InferEdge(rect);
        return true;
    }

    private static uint InferEdge(RECT taskbar)
    {
        if (taskbar.Width >= taskbar.Height)
        {
            return taskbar.Top + taskbar.Height / 2 < GetSystemMetrics(SM_CYSCREEN) / 2
                ? TaskbarPlacementCalculator.EdgeTop
                : TaskbarPlacementCalculator.EdgeBottom;
        }

        return taskbar.Left + taskbar.Width / 2 < GetSystemMetrics(SM_CXSCREEN) / 2
            ? TaskbarPlacementCalculator.EdgeLeft
            : TaskbarPlacementCalculator.EdgeRight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowExW(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    public static IntPtr TaskbarWindow() => FindWindowW("Shell_TrayWnd", null);

    public static string WindowClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        var length = GetClassNameW(hwnd, buffer, buffer.Capacity);
        return length <= 0 ? string.Empty : buffer.ToString(0, length);
    }

    // Left edge of the notification area (tray icons + clock) on the primary taskbar.
    public static bool TryGetTrayLeftEdge(out int left)
    {
        left = 0;
        var taskbar = TaskbarWindow();
        if (taskbar == IntPtr.Zero)
        {
            return false;
        }

        var tray = FindWindowExW(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (tray == IntPtr.Zero || !GetWindowRect(tray, out var rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            return false;
        }

        left = rect.Left;
        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    // Owning the overlay to the taskbar keeps it composited directly above the shell, so
    // clicking the taskbar cannot cover it for a frame (the shell raises itself above
    // unowned topmost windows until the next topmost re-assert).
    public static void AttachToTaskbar(IntPtr hwnd)
    {
        hwnd = RootWindow(hwnd);
        var taskbar = FindWindowW("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || GetWindow(hwnd, GW_OWNER) == taskbar)
        {
            return;
        }

        SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, taskbar);
    }

    public static void SetWindowLongPtr(IntPtr hwnd, int index, nint value)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr64(hwnd, index, value);
        }
        else
        {
            SetWindowLong32(hwnd, index, unchecked((int)value));
        }
    }

    public static void ApplyOverlayStyles(IntPtr hwnd)
    {
        hwnd = RootWindow(hwnd);
        var current = (int)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        var updated = (current | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW;
        if (updated != current)
        {
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, updated);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
    }

    public static void SetTopmostNoActivate(IntPtr hwnd)
    {
        hwnd = RootWindow(hwnd);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOOWNERZORDER);
    }

    public static void SetTopmostPosition(IntPtr hwnd, int x, int y, int width, int height)
    {
        hwnd = RootWindow(hwnd);
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOOWNERZORDER);
    }

    public static List<IntPtr> FindWindowsByTitlePrefix(string prefix)
    {
        var windows = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }
            var length = GetWindowTextLengthW(hwnd);
            if (length <= 0)
            {
                return true;
            }
            var builder = new StringBuilder(length + 1);
            GetWindowTextW(hwnd, builder, builder.Capacity);
            if (builder.ToString().StartsWith(prefix, StringComparison.Ordinal))
            {
                windows.Add(hwnd);
            }
            return true;
        }, IntPtr.Zero);
        return windows;
    }
}

public sealed class SingleInstanceGuard : IDisposable
{
    private IntPtr _handle;

    public bool Acquire(string mutexName)
    {
        _handle = NativeMethods.CreateMutexW(IntPtr.Zero, false, mutexName);
        var lastError = Marshal.GetLastWin32Error();
        if (_handle == IntPtr.Zero)
        {
            return true;
        }
        if (lastError == NativeMethods.ERROR_ALREADY_EXISTS)
        {
            Dispose();
            return false;
        }
        return true;
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
