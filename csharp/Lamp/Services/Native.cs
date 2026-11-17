using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Lamp.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Lamp.Services;

public static class Native
{
    const int VkLButton = 0x01;
    const int GwlExStyle = -20;
    const int DwmwaCloaked = 14;
    const int DwmwaExtendedFrameBounds = 9;
    const int MonitorDefaultToNearest = 2;
    const uint SwpNomove = 0x0001;
    const uint SwpNosize = 0x0002;
    const uint SwpNoactivate = 0x0010;
    const int SwShownoactivate = 4;
    const int QunsRunningD3dFullScreen = 3;
    const int QunsPresentationMode = 4;
    static readonly IntPtr HwndTopmost = new(-1);
    static readonly HashSet<string> ShellClasses =
    [
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "DV2ControlHost", "CEF-OSC-WIDGET",
    ];

    public static bool LeftButtonDown() => (GetAsyncKeyState(VkLButton) & 0x8000) != 0;

    public static Point CursorDip(VisualDpi dpi)
    {
        GetCursorPos(out var p);
        return new Point(p.X / dpi.ScaleX, p.Y / dpi.ScaleY);
    }

    public static Rect WorkAreaDip(Point dipPoint, VisualDpi dpi)
    {
        var px = new Drawing.Point((int)Math.Round(dipPoint.X * dpi.ScaleX), (int)Math.Round(dipPoint.Y * dpi.ScaleY));
        var area = Forms.Screen.FromPoint(px).WorkingArea;
        return PixelRectToDip(area, dpi);
    }

    public static Rect MonitorAreaDip(Window window, bool fullMonitor)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        Forms.Screen screen;
        if (hwnd != IntPtr.Zero)
            screen = Forms.Screen.FromHandle(hwnd);
        else
        {
            GetCursorPos(out var p);
            screen = Forms.Screen.FromPoint(new Drawing.Point(p.X, p.Y));
        }
        var px = fullMonitor ? screen.Bounds : screen.WorkingArea;
        return PixelRectToDip(px, DpiOf(window));
    }

    static Rect PixelRectToDip(Drawing.Rectangle px, VisualDpi dpi)
    {
        var sx = dpi.ScaleX == 0 ? 1 : dpi.ScaleX;
        var sy = dpi.ScaleY == 0 ? 1 : dpi.ScaleY;
        return new Rect(px.Left / sx, px.Top / sy, px.Width / sx, px.Height / sy);
    }

    public static VisualDpi DpiOf(Window window)
    {
        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget is { } ct)
            return new VisualDpi(ct.TransformToDevice.M11, ct.TransformToDevice.M22);
        var dpi = VisualTreeHelperDpi(window);
        return dpi;
    }

    static VisualDpi VisualTreeHelperDpi(Window window)
    {
        try
        {
            var d = System.Windows.Media.VisualTreeHelper.GetDpi(window);
            return new VisualDpi(d.DpiScaleX, d.DpiScaleY);
        }
        catch
        {
            return new VisualDpi(1, 1);
        }
    }

    public static void ShowInactive(Window window)
    {
        window.ShowActivated = false;
        if (!window.IsVisible) window.Show();
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        ShowWindow(hwnd, SwShownoactivate);
        LiftTopmost(window);
    }

    public static void LiftTopmost(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate);
    }

    public static bool FullscreenCover(Window island)
    {
        try
        {
            if (GameOrPresentation()) return true;
            var islandHwnd = new WindowInteropHelper(island).Handle;
            var found = false;
            EnumWindows((hwnd, _) =>
            {
                if (found) return false;
                try
                {
                    if (WindowCovers(hwnd, islandHwnd)) found = true;
                }
                catch
                {
                    /* the window can close while we walk */
                }
                return !found;
            }, IntPtr.Zero);
            return found;
        }
        catch
        {
            return false;
        }
    }

    static bool GameOrPresentation()
    {
        if (SHQueryUserNotificationState(out var state) != 0) return false;
        return state is QunsRunningD3dFullScreen or QunsPresentationMode;
    }

    static bool WindowCovers(IntPtr hwnd, IntPtr island)
    {
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId) return false;
        if (IsCloaked(hwnd)) return false;
        var ex = (long)GetWindowLongPtr(hwnd, GwlExStyle);
        if ((ex & 0x20) != 0) return false; // WS_EX_TRANSPARENT
        var name = ClassName(hwnd);
        if (string.IsNullOrEmpty(name) || ShellClasses.Contains(name)) return false;
        if (!SameMonitor(hwnd, island)) return false;
        if (!MonitorAreas(hwnd, out var screen, out var work)) return false;
        if (Covers(WindowRect(hwnd), screen) || Covers(ExtendedFrame(hwnd), screen) || Covers(ClientRect(hwnd), screen))
            return true;
        if (IsZoomed(hwnd)) return true;
        return Covers(WindowRect(hwnd), work) || Covers(ExtendedFrame(hwnd), work) || Covers(ClientRect(hwnd), work);
    }

    static bool Covers(RECT? rect, RECT monitor)
    {
        if (rect is null) return false;
        var r = rect.Value;
        var width = monitor.Right - monitor.Left;
        var height = monitor.Bottom - monitor.Top;
        var tolX = Math.Min(48, Math.Max(16, (int)Math.Round(width * 0.02)));
        var tolY = Math.Min(32, Math.Max(16, (int)Math.Round(height * 0.015)));
        return r.Left <= monitor.Left + tolX
            && r.Top <= monitor.Top + tolY
            && r.Right >= monitor.Right - tolX
            && r.Bottom >= monitor.Bottom - tolY;
    }

    static bool SameMonitor(IntPtr a, IntPtr b)
    {
        if (b == IntPtr.Zero) return true;
        if (!MonitorAreas(a, out var sa, out _) || !MonitorAreas(b, out var sb, out _)) return false;
        return sa.Equals(sb);
    }

    static bool MonitorAreas(IntPtr hwnd, out RECT screen, out RECT work)
    {
        screen = default;
        work = default;
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return false;
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;
        screen = info.Monitor;
        work = info.Work;
        return true;
    }

    static RECT? WindowRect(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var r)) return null;
        if (r.Right - r.Left < 200 || r.Bottom - r.Top < 200) return null;
        return r;
    }

    static RECT? ClientRect(IntPtr hwnd)
    {
        if (!GetClientRect(hwnd, out var r)) return null;
        var origin = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(hwnd, ref origin)) return null;
        var width = r.Right - r.Left;
        var height = r.Bottom - r.Top;
        if (width < 200 || height < 200) return null;
        return new RECT { Left = origin.X, Top = origin.Y, Right = origin.X + width, Bottom = origin.Y + height };
    }

    static RECT? ExtendedFrame(IntPtr hwnd)
    {
        var r = new RECT();
        if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out r, Marshal.SizeOf<RECT>()) != 0) return null;
        if (r.Right - r.Left < 200 || r.Bottom - r.Top < 200) return null;
        return r;
    }

    static bool IsCloaked(IntPtr hwnd)
    {
        return DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int cloaked, 4) == 0 && cloaked != 0;
    }

    static string ClassName(IntPtr hwnd)
    {
        var buf = new char[256];
        var n = GetClassName(hwnd, buf, buf.Length);
        return n <= 0 ? "" : new string(buf, 0, n);
    }

    public static IslandEdge ChooseEdge(Point cursor, Rect work, IslandEdge? held = null)
    {
        var ranks = new (IslandEdge Edge, double Dist)[]
        {
            (IslandEdge.top, Math.Abs(cursor.Y - work.Top)),
            (IslandEdge.left, Math.Abs(cursor.X - work.Left)),
            (IslandEdge.right, Math.Abs(cursor.X - work.Right)),
        };
        Array.Sort(ranks, (a, b) => a.Dist.CompareTo(b.Dist));
        var edge = ranks[0].Edge;
        if (held is { } keep && keep != edge)
        {
            var found = Array.Find(ranks, r => r.Edge == keep);
            if (found.Dist <= 112 && found.Dist < ranks[0].Dist + 36) edge = keep;
        }
        return edge;
    }

    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, char[] lpClassName, int nMaxCount);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int pquns);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MONITORINFO
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public int Flags;
    }
}

public readonly record struct VisualDpi(double ScaleX, double ScaleY);
