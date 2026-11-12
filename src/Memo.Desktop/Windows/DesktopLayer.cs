using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Memo.Desktop.Windows;

internal sealed class DesktopLayer : IDisposable
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int WsMinimizeBox = 0x00020000;
    private const int WsExAppWindow = 0x00040000;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTopmost = 0x00000008;
    private const uint Foreground = 0x0003;
    private const uint ObjectReorder = 0x8004;
    private const uint SkipOwnProcess = 2;
    private const uint PreviousWindow = 3;
    private const uint NextWindow = 2;
    private const uint NoMoveOrSize = 0x0003;
    private const uint NoActivate = 0x0010;
    private const uint NoOwnerZOrder = 0x0200;

    private static readonly uint ShellRestarted = RegisterWindowMessage("TaskbarCreated");

    private readonly HwndSource source;
    private readonly WinEventHandler callback;
    private readonly nint foregroundHook;
    private nint reorderHook;
    private uint shellProcess;
    private bool pending;
    private bool disposed;
    private bool acceptingInput;
    private nint previousForeground;
    private nint? wakeForeground;
    private bool pinned;

    public DesktopLayer(HwndSource source)
    {
        this.source = source;
        callback = OnWinEvent;
        source.AddHook(WindowMessage);
        var handle = source.Handle;
        SetWindowLong(handle, GwlStyle, GetWindowLong(handle, GwlStyle) & ~WsMinimizeBox);
        var extended = GetWindowLong(handle, GwlExStyle) & ~WsExAppWindow;
        SetWindowLong(handle, GwlExStyle, extended | WsExNoActivate | WsExToolWindow);
        foregroundHook = SetWinEventHook(Foreground, Foreground, 0, callback, 0, 0, SkipOwnProcess);
    }

    public void BeginInput()
    {
        if (acceptingInput)
            return;
        previousForeground = GetForegroundWindow();
        acceptingInput = true;
        var hwnd = source.Handle;
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) & ~WsExNoActivate);
    }

    public void EndInput()
    {
        acceptingInput = false;
        var hwnd = source.Handle;
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExNoActivate);
        if (GetForegroundWindow() == hwnd)
        {
            var target = previousForeground;
            if (target == 0 || target == hwnd || !IsWindowVisible(target) || IsIconic(target))
                target = GetShellWindow();
            if (target != 0)
                SetForegroundWindow(target);
        }

        previousForeground = 0;
        wakeForeground = null;
        RequestPlacement();
    }

    public void Wake()
    {
        wakeForeground = GetForegroundWindow();
        SetWindowPos(source.Handle, pinned ? -1 : 0, 0, 0, 0, 0, NoMoveOrSize | NoActivate | NoOwnerZOrder);
    }

    public void SetPinned(bool value)
    {
        if (pinned == value)
            return;
        pinned = value;
        wakeForeground = null;
        SetWindowPos(source.Handle, value ? -1 : -2, 0, 0, 0, 0, NoMoveOrSize | NoActivate | NoOwnerZOrder);
        RequestPlacement();
    }

    public void RequestPlacement()
    {
        if (pending || disposed)
            return;

        pending = true;
        source.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            pending = false;
            if (!disposed)
                PlaceAboveDesktop();
        });
    }

    private void PlaceAboveDesktop()
    {
        var handle = source.Handle;
        if (handle == 0)
            return;

        if (IsIconic(handle))
            ShowWindow(handle, 4); // SW_SHOWNOACTIVATE restores without taking focus.

        if (pinned)
        {
            if ((GetWindowLong(handle, GwlExStyle) & WsExTopmost) == 0)
                SetWindowPos(handle, -1, 0, 0, 0, 0, NoMoveOrSize | NoActivate | NoOwnerZOrder);
            return;
        }

        var foreground = GetForegroundWindow();
        if (wakeForeground == foreground || (acceptingInput && foreground == handle))
            return;
        wakeForeground = null;

        var shell = GetShellWindow();
        if (shell == 0)
            return;

        GetWindowThreadProcessId(shell, out var processId);
        if (shellProcess != processId)
        {
            if (reorderHook != 0)
                UnhookWinEvent(reorderHook);
            shellProcess = processId;
            reorderHook = SetWinEventHook(ObjectReorder, ObjectReorder, 0, callback, shellProcess, 0, SkipOwnProcess);
        }

        // Explorer may host its desktop view in a sibling of Progman.
        // Use whichever visible desktop surface is highest in the current stack.
        var desktop = shell;
        for (var window = GetTopWindow(0); window != 0 && window != shell; window = GetWindow(window, NextWindow))
        {
            if (IsWindowVisible(window)
                && FindWindowEx(window, 0, "SHELLDLL_DefView", null) != 0)
            {
                desktop = window;
                break;
            }
        }

        var above = GetWindow(desktop, PreviousWindow);
        if (above != source.Handle)
        {
            // Inserting behind a topmost window can promote us to the topmost band.
            if (above != 0 && (GetWindowLong(above, GwlExStyle) & WsExTopmost) != 0)
                above = 0;

            SetWindowPos(source.Handle, above, 0, 0, 0, 0, NoMoveOrSize | NoActivate | NoOwnerZOrder);
        }

    }

    private void OnWinEvent(nint hook, uint type, nint hwnd, int objectId, int childId, uint thread, uint time)
    {
        if (type == Foreground || (objectId == 0 && childId == 0))
            RequestPlacement();
    }

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)message == ShellRestarted || (message == 0x0005 && wParam == 1)) // WM_SIZE / SIZE_MINIMIZED
            RequestPlacement();

        if (message == 0x0021 && !acceptingInput) // WM_MOUSEACTIVATE
        {
            handled = true;
            return 3; // MA_NOACTIVATE
        }

        if (message == 0x0112 && ((long)wParam & 0xfff0) is 0xf020 or 0xf030 or 0xf060)
        {
            handled = true; // WM_SYSCOMMAND: minimize, maximize, close
        }

        return 0;
    }

    public void Dispose()
    {
        disposed = true;
        if (foregroundHook != 0)
            UnhookWinEvent(foregroundHook);
        if (reorderHook != 0)
            UnhookWinEvent(reorderHook);
        if (!source.IsDisposed)
            source.RemoveHook(WindowMessage);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);

    private delegate void WinEventHandler(nint hook, uint type, nint hwnd, int objectId, int childId, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventHandler handler, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint process);

    [DllImport("user32.dll")]
    private static extern nint GetTopWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint after, string className, string? title);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint window, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
