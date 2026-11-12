using System.Drawing;
using System.IO;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Memo.Desktop.Windows;

internal sealed class MemoTray : IDisposable
{
    private readonly Stream iconStream;
    private readonly Icon icon;
    private readonly Forms.ContextMenuStrip menu;
    private readonly Forms.NotifyIcon notifyIcon;

    public MemoTray(Dispatcher dispatcher, Action openSettings, Action exit)
    {
        iconStream = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/白术便签;component/Assets/BaizhuMemo.ico"))!.Stream;
        icon = new Icon(iconStream);
        menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开设置", null, (_, _) => dispatcher.BeginInvoke(openSettings));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出应用", null, (_, _) => dispatcher.BeginInvoke(exit));

        notifyIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "白术便签",
            ContextMenuStrip = menu
        };
        notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                openSettings();
        };
        notifyIcon.Visible = true;
    }

    public void Dispose()
    {
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        menu.Dispose();
        icon.Dispose();
        iconStream.Dispose();
    }
}
