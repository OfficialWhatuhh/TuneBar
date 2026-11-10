using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TuneBar
{
    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_TOOLWINDOW = 0x00000080;   // hide from Alt+Tab
        public const long WS_EX_NOACTIVATE = 0x08000000;   // clicking us doesn't steal focus

        public static readonly IntPtr HWND_TOPMOST = new(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;

        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const uint MONITORINFOF_PRIMARY = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string? className, string? windowName);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        /// <summary>
        /// Finds the main taskbar. When auto-hide is on and it's tucked away, Windows slides
        /// it almost completely off the screen, which is how we know to hide too.
        /// </summary>
        public static bool TryGetTaskbar(out RECT taskbar, out RECT monitor, out bool visible)
        {
            taskbar = default;
            monitor = default;
            visible = false;

            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero || !GetWindowRect(tray, out taskbar)) return false;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            IntPtr mon = MonitorFromWindow(tray, MONITOR_DEFAULTTONEAREST);
            if (!GetMonitorInfo(mon, ref info)) return false;

            monitor = info.rcMonitor;
            visible = IsWindowVisible(tray);
            return true;
        }

        public static bool TryGetTaskbarHandle(out IntPtr tray, out RECT rect)
        {
            rect = default;
            tray = FindWindow("Shell_TrayWnd", null);
            return tray != IntPtr.Zero && GetWindowRect(tray, out rect);
        }

        public static void AddExtendedStyle(IntPtr hwnd, long flags)
        {
            if (IntPtr.Size == 8)
            {
                long current = GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64();
                SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(current | flags));
            }
            else
            {
                int current = GetWindowLong32(hwnd, GWL_EXSTYLE);
                SetWindowLong32(hwnd, GWL_EXSTYLE, current | (int)flags);
            }
        }

        public static void MakeTopmost(IntPtr hwnd) =>
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        public static string ClassOf(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string ForegroundClass() => ClassOf(GetForegroundWindow());

        /// <summary>True when a game/video is fullscreen on the primary monitor.</summary>
        public static bool IsForegroundFullscreen(IntPtr self)
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == self || fg == GetShellWindow() || fg == GetDesktopWindow())
                return false;

            string cls = ClassOf(fg);
            if (cls is "WorkerW" or "Progman" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                return false;

            if (!GetWindowRect(fg, out RECT r)) return false;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            IntPtr monitor = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
            if (!GetMonitorInfo(monitor, ref info)) return false;
            if ((info.dwFlags & MONITORINFOF_PRIMARY) == 0) return false;

            return r.Left <= info.rcMonitor.Left && r.Top <= info.rcMonitor.Top &&
                   r.Right >= info.rcMonitor.Right && r.Bottom >= info.rcMonitor.Bottom;
        }
    }
}
