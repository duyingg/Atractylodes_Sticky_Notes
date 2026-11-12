using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Memo.Desktop.Models;

namespace Memo.Desktop.Views;

public partial class TaskReader : UserControl
{
    public TaskReader() => InitializeComponent();
    internal event Action? Closed;

    internal void ShowTask(MemoTask item)
    {
        FullText.Text = item.Text;
        FullText.CaretIndex = 0;
        FullText.ScrollToHome();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (IsVisible)
                FullText.Focus();
        });
    }

    private void Close(object sender, RoutedEventArgs e) => Closed?.Invoke();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Closed?.Invoke();
        }
    }
}
