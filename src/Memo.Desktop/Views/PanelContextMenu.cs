using System.Windows.Controls;
using System.Windows.Input;

namespace Memo.Desktop.Views;

internal static class PanelContextMenu
{
    internal static ContextMenu Create(Action close, Action exit, TextBox? editor = null)
    {
        var menu = new ContextMenu();
        menu.SetResourceReference(Control.BackgroundProperty, "Surface");
        menu.SetResourceReference(Control.BorderBrushProperty, "Edge");

        MenuItem Add(string text)
        {
            var item = new MenuItem { Header = text };
            item.SetResourceReference(Control.ForegroundProperty, "Ink");
            menu.Items.Add(item);
            return item;
        }

        if (editor is not null)
        {
            foreach (var (text, command) in new[]
            {
                ("剪切", ApplicationCommands.Cut), ("复制", ApplicationCommands.Copy), ("粘贴", ApplicationCommands.Paste)
            })
            {
                var item = Add(text);
                item.Command = command;
                item.CommandTarget = editor;
            }
            menu.Items.Add(new Separator());
        }

        Add("关闭该栏目").Click += (_, _) => close();
        Add("退出程序").Click += (_, _) => exit();
        return menu;
    }
}
