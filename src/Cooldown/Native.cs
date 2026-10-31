using System;
using System.Runtime.InteropServices;

namespace Cooldown
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        public const uint ABE_LEFT = 0, ABE_TOP = 1, ABE_RIGHT = 2, ABE_BOTTOM = 3;
        const uint ABM_GETTASKBARPOS = 5;

        public const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPSIBLINGS = 0x04000000;
        const int GWL_EXSTYLE = -20;
        public const long WS_EX_TOOLWINDOW = 0x80;

        static readonly IntPtr HWND_TOP = IntPtr.Zero;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const uint SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10;

        [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong32(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GetCursorPos(out POINT point);

        public static IntPtr FindTaskbar() => FindWindow("Shell_TrayWnd", null);

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor, rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        public static RECT? MonitorBounds(IntPtr hwnd)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
            return monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info) ? info.rcMonitor : (RECT?)null;
        }

        public static bool TryGetTaskbar(out RECT rect, out uint edge)
        {
            var data = new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)) };
            var ok = SHAppBarMessage(ABM_GETTASKBARPOS, ref data) != UIntPtr.Zero;
            rect = data.rc;
            edge = data.uEdge;
            return ok && rect.Width > 0 && rect.Height > 0;
        }

        public static RECT? GetTrayRect()
        {
            var taskbar = FindTaskbar();
            if (taskbar == IntPtr.Zero) return null;
            var tray = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            if (tray == IntPtr.Zero || !GetWindowRect(tray, out var r) || r.Width <= 0) return null;
            return r;
        }

        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

        public static bool IsTopChild(IntPtr hwnd) => GetWindow(hwnd, 3 /* GW_HWNDPREV */) == IntPtr.Zero;

        public static void PlaceChildOnTop(IntPtr hwnd, int x, int y) =>
            SetWindowPos(hwnd, HWND_TOP, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);

        public static void MoveBelowTaskbar(IntPtr hwnd, int x, int y)
        {
            var taskbar = FindTaskbar();
            SetWindowPos(hwnd, taskbar == IntPtr.Zero ? HWND_TOPMOST : taskbar, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }

        public static void SetBoundsBelowTaskbar(IntPtr hwnd, int x, int y, int width, int height)
        {
            var taskbar = FindTaskbar();
            SetWindowPos(hwnd, taskbar == IntPtr.Zero ? HWND_TOPMOST : taskbar, x, y, width, height, SWP_NOACTIVATE);
        }

        public static bool CursorInside(IntPtr hwnd) =>
            GetCursorPos(out var p) && GetWindowRect(hwnd, out var r) &&
            p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;

        public static void AddExStyle(IntPtr hwnd, long flags) => SetStyle(hwnd, GWL_EXSTYLE, style => style | flags);

        static void SetStyle(IntPtr hwnd, int index, Func<long, long> change)
        {
            if (IntPtr.Size == 8)
            {
                var style = GetWindowLongPtr64(hwnd, index).ToInt64();
                SetWindowLongPtr64(hwnd, index, new IntPtr(change(style)));
            }
            else
            {
                var style = (long)(uint)GetWindowLong32(hwnd, index);
                SetWindowLong32(hwnd, index, unchecked((int)change(style)));
            }
        }

        public static double Scale(IntPtr hwnd)
        {
            try
            {
                var dpi = GetDpiForWindow(hwnd);
                return dpi > 0 ? dpi / 96.0 : 1.0;
            }
            catch (EntryPointNotFoundException)
            {
                return 1.0;
            }
        }
    }
}
