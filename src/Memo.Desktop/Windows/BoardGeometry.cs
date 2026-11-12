using System.Runtime.InteropServices;
using Memo.Desktop.Models;

namespace Memo.Desktop.Windows;

internal static class BoardGeometry
{
    private const uint NearestMonitor = 2;
    private const uint NoActivateOrZOrder = 0x14;

    public static void Apply(nint window, BoardLayout layout)
    {
        var requested = new Point(layout.X ?? 0, layout.Y ?? 0);
        var monitor = MonitorFromPoint(requested, NearestMonitor);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return;

        var area = info.Work;
        var x = Math.Clamp(requested.X, area.Left, area.Right - 1);
        var y = Math.Clamp(requested.Y, area.Top, area.Bottom - 1);

        if (MonitorFromWindow(window, NearestMonitor) != monitor)
            SetWindowPos(window, 0, x, y, 1, 1, NoActivateOrZOrder);

        var scale = Math.Max(96u, GetDpiForWindow(window)) / 96d;
        var width = Math.Min((int)Math.Round(layout.Edge * scale), area.Right - area.Left);
        var height = Math.Min((int)Math.Round(layout.EffectiveHeight * scale), area.Bottom - area.Top);
        var margin = (int)Math.Round(24 * scale);
        var left = Math.Clamp(layout.X ?? area.Right - width - margin, area.Left, area.Right - width);
        var top = Math.Clamp(layout.Y ?? area.Top + margin, area.Top, area.Bottom - height);
        SetBounds(window, new Rect { Left = left, Top = top, Right = left + width, Bottom = top + height }, scale);
    }

    internal static void SetBounds(nint window, Rect bounds, double scale)
    {
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        SetWindowPos(window, 0, bounds.Left, bounds.Top, width, height, NoActivateOrZOrder);
        var diameter = (int)Math.Round(24 * scale);
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
        if (region != 0 && SetWindowRgn(window, region, true) == 0)
            DeleteObject(region);
    }

    internal static Rect Resize(Placement start, ResizeEdges edges, Point delta)
    {
        var bounds = start.Board;
        var scale = start.Scale;
        var min = (int)Math.Round(BoardLayout.MinimumEdge * scale);
        var max = (int)Math.Round(BoardLayout.MaximumEdge * scale);
        var area = start.Work;

        if (edges.HasFlag(ResizeEdges.Left))
            bounds.Left = Math.Clamp(bounds.Left + delta.X, Math.Max(area.Left, bounds.Right - max), Math.Max(area.Left, bounds.Right - min));
        if (edges.HasFlag(ResizeEdges.Right))
            bounds.Right = Math.Clamp(bounds.Right + delta.X, Math.Min(area.Right, bounds.Left + min), Math.Min(area.Right, bounds.Left + max));
        if (edges.HasFlag(ResizeEdges.Top))
            bounds.Top = Math.Clamp(bounds.Top + delta.Y, Math.Max(area.Top, bounds.Bottom - max), Math.Max(area.Top, bounds.Bottom - min));
        if (edges.HasFlag(ResizeEdges.Bottom))
            bounds.Bottom = Math.Clamp(bounds.Bottom + delta.Y, Math.Min(area.Bottom, bounds.Top + min), Math.Min(area.Bottom, bounds.Top + max));
        return bounds;
    }

    internal static BoardLayout AdjacentNoteLayout(Placement placement)
    {
        var area = placement.Work;
        var board = placement.Board;
        var scale = placement.Scale;
        var gap = (int)Math.Round(12 * scale);
        var desired = (int)Math.Round(320 * scale);
        var minimum = (int)Math.Round(160 * scale);
        var leftRoom = Math.Max(0, board.Left - area.Left - gap);
        var rightRoom = Math.Max(0, area.Right - board.Right - gap);
        var topRoom = Math.Max(0, board.Top - area.Top - gap);
        var bottomRoom = Math.Max(0, area.Bottom - board.Bottom - gap);
        var horizontal = Math.Max(leftRoom, rightRoom);
        var vertical = Math.Max(topRoom, bottomRoom);

        if (horizontal >= vertical)
        {
            var width = Math.Clamp(horizontal, minimum, desired);
            var x = leftRoom >= rightRoom ? Math.Max(area.Left, board.Left - gap - width)
                : Math.Clamp(board.Right + gap, area.Left, Math.Max(area.Left, area.Right - width));
            return new BoardLayout(width / scale, x, board.Top, 320);
        }

        var height = Math.Clamp(vertical, minimum, desired);
        var y = topRoom >= bottomRoom ? Math.Max(area.Top, board.Top - gap - height)
            : Math.Clamp(board.Bottom + gap, area.Top, Math.Max(area.Top, area.Bottom - height));
        return new BoardLayout(320, board.Left, y, height / scale);
    }

    public static Placement? CurrentPlacement(nint window)
    {
        var monitor = MonitorFromWindow(window, NearestMonitor);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info)
            ? new Placement(info.Monitor, info.Work, Bounds(window), GetDpiForWindow(window), (info.Flags & 1) != 0)
            : null;
    }

    [Flags]
    internal enum ResizeEdges { Left = 1, Top = 2, Right = 4, Bottom = 8 }

    internal readonly record struct Placement(Rect Monitor, Rect Work, Rect Board, uint Dpi, bool IsPrimary)
    {
        public double Scale => Math.Max(96u, Dpi) / 96d;
    }

    public static Point Cursor()
    {
        GetCursorPos(out var point);
        return point;
    }

    public static Rect Bounds(nint window)
    {
        GetWindowRect(window, out var rect);
        return rect;
    }

    public static void Move(nint window, int x, int y) =>
        SetWindowPos(window, 0, x, y, 0, 0, NoActivateOrZOrder | 1);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct Point(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
    }

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint window, nint region, bool redraw);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
