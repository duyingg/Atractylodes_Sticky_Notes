using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Memo.Desktop.Models;
using Memo.Desktop.ViewModels;

namespace Memo.Desktop.Views;

public partial class TaskForm : UserControl
{
    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact), typeof(bool), typeof(TaskForm));

    private MemoTask? editing;
    private bool saving;

    public TaskForm() => InitializeComponent();

    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    internal event Action<Guid, bool>? Saved;
    internal event Action? Cancelled;

    internal void Begin(MemoTask? task)
    {
        editing = task;
        Input.Text = task?.Text ?? "";
        UpdateCount(this, null);
        ClearError();
        FocusInput();
    }

    internal void FocusInput() => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
    {
        if (IsVisible && Input.IsEnabled)
        {
            Input.Focus();
            Input.CaretIndex = Input.Text.Length;
        }
    });

    internal void RequestCancel()
    {
        if (!saving)
            Cancelled?.Invoke();
    }

    private void UpdateCount(object sender, TextChangedEventArgs? e)
    {
        if (Count is not null)
            Count.Text = $"{Input.Text.Length} / {MemoDocument.MaximumTaskLength}";
    }

    private void ClearError()
    {
        ErrorBanner.Visibility = Visibility.Collapsed;
        Input.ClearValue(BorderBrushProperty);
        SaveButton.Content = "保存任务";
    }

    private void Cancel(object sender, RoutedEventArgs e) => RequestCancel();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !saving)
        {
            e.Handled = true;
            RequestCancel();
        }
    }

    private async void Save(object sender, RoutedEventArgs e)
    {
        if (saving || DataContext is not MemoViewModel model)
            return;

        saving = true;
        SaveButton.IsEnabled = CancelButton.IsEnabled = Input.IsEnabled = false;
        var error = await model.SaveTaskAsync(editing, Input.Text);
        saving = false;
        SaveButton.IsEnabled = CancelButton.IsEnabled = Input.IsEnabled = true;
        if (error is null)
        {
            Saved?.Invoke(editing?.Id ?? model.Tasks.Last(task => !task.IsCompleted).Id, editing is null);
            return;
        }

        ErrorBanner.Visibility = Visibility.Visible;
        ErrorText.Text = error.StartsWith("保存失败", StringComparison.Ordinal)
            ? "保存失败，输入已保留。请检查存储目录后重试。" : error;
        Input.SetResourceReference(BorderBrushProperty, "Danger");
        SaveButton.Content = "重试保存";
        FocusInput();
    }
}
