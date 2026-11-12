using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Memo.Desktop.Models;

namespace Memo.Desktop.Windows;

internal sealed class GlobalShortcuts : IDisposable
{
    private const int HotkeyMessage = 0x0312;
    private const int FirstId = 0x301;
    private const uint NoRepeat = 0x4000;
    private readonly HwndSource source = new(new HwndSourceParameters("Memo.Shortcuts") { ParentWindow = -3, WindowStyle = 0 });
    private readonly HashSet<ShortcutAction> registered = [];
    private readonly Dictionary<ShortcutAction, string> statuses = [];
    private ShortcutKeys? keys;
    private bool enabled;

    public GlobalShortcuts() => source.AddHook(HandleMessage);

    public event Action<ShortcutAction>? Invoked;

    public IReadOnlyDictionary<ShortcutAction, string> Configure(BehaviorOptions behavior)
    {
        var next = behavior.EffectiveKeys;
        if (keys != next || enabled != behavior.ShortcutsEnabled)
            UnregisterAll();

        keys = next;
        enabled = behavior.ShortcutsEnabled;
        foreach (var action in Enum.GetValues<ShortcutAction>())
        {
            if (!enabled)
                statuses[action] = "已关闭";
            else if (!ShortcutChord.TryParse(next.Get(action), out var chord))
                statuses[action] = "组合无效";
            else if (registered.Contains(action)
                || RegisterHotKey(source.Handle, FirstId + (int)action, chord.Modifiers | NoRepeat, chord.Key))
            {
                registered.Add(action);
                statuses[action] = "已生效";
            }
            else
            {
                statuses[action] = "被占用或不可用";
            }
        }

        return statuses;
    }

    private nint HandleMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        var action = (ShortcutAction)((int)wParam - FirstId);
        if (message == HotkeyMessage && registered.Contains(action))
        {
            handled = true;
            source.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (!source.IsDisposed && registered.Contains(action))
                    Invoked?.Invoke(action);
            });
        }
        return 0;
    }

    private void UnregisterAll()
    {
        foreach (var action in registered)
            UnregisterHotKey(source.Handle, FirstId + (int)action);
        registered.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        source.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint hwnd, int id);
}
