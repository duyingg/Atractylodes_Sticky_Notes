using System.Windows;
using System.Windows.Controls;
using Memo.Desktop.Models;
using Memo.Desktop.ViewModels;

namespace Memo.Desktop.Views;

public partial class TaskWorkspace : UserControl
{
    public TaskWorkspace()
    {
        InitializeComponent();
        Tasks.OpenRequested += OpenTask;
        Reader.Closed += EndEdit;
        Form.Cancelled += EndEdit;
        Form.Saved += Saved;
    }

    internal event Action? PermissionsRequested;

    private void OpenPermissions(object sender, RoutedEventArgs e) => PermissionsRequested?.Invoke();

    private void OpenTask(MemoTask task)
    {
        if (DataContext is MemoViewModel { CanEditTasks: true })
        {
            BeginEdit(task);
            return;
        }
        ListPage.Visibility = Visibility.Collapsed;
        Reader.Visibility = Visibility.Visible;
        Reader.ShowTask(task);
    }

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        if (Tasks is not null && Enum.TryParse<TaskFilter>(((RadioButton)sender).Tag as string, out var filter))
            Tasks.SetFilter(filter);
    }

    private void Add(object sender, RoutedEventArgs e) => BeginEdit(null);

    internal void QuickAdd()
    {
        if (EditorPage.Visibility == Visibility.Visible)
            Form.FocusInput();
        else
            BeginEdit(null);
    }

    private void BeginEdit(MemoTask? task)
    {
        if (DataContext is not MemoViewModel { CanEditTasks: true })
            return;
        EditorTitle.Text = task is null ? "新增任务" : "编辑任务";
        ListPage.Visibility = Visibility.Collapsed;
        Reader.Visibility = Visibility.Collapsed;
        EditorPage.Visibility = Visibility.Visible;
        Form.Begin(task);
    }

    private void CancelEdit(object sender, RoutedEventArgs e) => Form.RequestCancel();

    private void EndEdit()
    {
        EditorPage.Visibility = Visibility.Collapsed;
        Reader.Visibility = Visibility.Collapsed;
        ListPage.Visibility = Visibility.Visible;
        AddButton.Focus();
    }

    private void Saved(Guid id, bool added)
    {
        EndEdit();
        if (added)
            AllFilter.IsChecked = true;
        Tasks.ShowTask(id);
    }
}
