using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Memo.Desktop.Models;
using Memo.Desktop.ViewModels;

namespace Memo.Desktop.Views;

internal enum TaskFilter { All, Open, Completed }

public partial class TaskList : UserControl
{
    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact), typeof(bool), typeof(TaskList));

    private MemoViewModel? model;
    private Row[] items = [];
    private TaskFilter filter;
    private readonly DispatcherTimer scrollTimer;
    private Row? dragSource;
    private ListBoxItem? dragContainer;
    private Row? dropTarget;
    private bool dropAfter;

    public TaskList()
    {
        InitializeComponent();
        DataContextChanged += OnModelChanged;
        scrollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(45), DispatcherPriority.Input, ScrollWhileDragging, Dispatcher);
        scrollTimer.Stop();
        Unloaded += (_, _) => ClearDrag();
    }

    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    internal event Action<MemoTask>? OpenRequested;

    internal void SetFilter(TaskFilter next)
    {
        if (filter == next)
            return;
        filter = next;
        Refresh(preserveScroll: false);
    }

    internal void ShowTask(Guid id)
    {
        var item = items.FirstOrDefault(row => row.Task.Id == id);
        if (item is null)
            return;
        foreach (var row in items)
            row.Highlighted = row == item;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Rows.ScrollIntoView(item));
    }

    private void OnModelChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (model is not null)
            model.PropertyChanged -= OnStateChanged;
        model = e.NewValue as MemoViewModel;
        if (model is not null)
            model.PropertyChanged += OnStateChanged;
        Refresh();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MemoViewModel.Tasks))
            Refresh();
    }

    private void Refresh(bool preserveScroll = true)
    {
        ClearDrag();
        var offset = preserveScroll ? Viewer()?.VerticalOffset ?? 0 : 0;
        var confirming = items.FirstOrDefault(row => row.Confirming)?.Task.Id;
        var highlighted = items.FirstOrDefault(row => row.Highlighted)?.Task.Id;
        var visible = (model?.Tasks ?? []).Where(task => filter switch
        {
            TaskFilter.Open => !task.IsCompleted,
            TaskFilter.Completed => task.IsCompleted,
            _ => true
        });
        items = visible.Select(task => new Row(task)
        {
            Confirming = task.Id == confirming,
            Highlighted = task.Id == highlighted
        }).ToArray();
        Rows.ItemsSource = items;
        if (offset > 0)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Viewer()?.ScrollToVerticalOffset(offset));
        EmptyText.Text = filter == TaskFilter.All ? "还没有任务" : "当前筛选下没有任务。";
        EmptyText.Visibility = !Compact && items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Text = "";
    }

    private static Row Item(object sender) => (Row)((FrameworkElement)sender).DataContext;

    private async void Toggle(object sender, RoutedEventArgs e)
    {
        var check = (CheckBox)sender;
        // The row reflects the committed task. Keep the old check state if saving fails.
        check.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
        if (model is not null)
            ErrorText.Text = await model.ToggleAsync(Item(check).Task) ?? "";
    }

    private void OpenTask(object sender, RoutedEventArgs e) => OpenRequested?.Invoke(Item(sender).Task);

    private void AskDelete(object sender, RoutedEventArgs e)
    {
        if (Compact)
            return;
        var selected = Item(sender);
        foreach (var row in items)
            row.Confirming = row == selected;
        ErrorText.Text = "";
        Rows.ScrollIntoView(selected);
    }

    private void CancelDelete(object sender, RoutedEventArgs e)
    {
        Item(sender).Confirming = false;
        ErrorText.Text = "";
    }

    private async void ConfirmDelete(object sender, RoutedEventArgs e)
    {
        if (!Compact && model is not null)
            ErrorText.Text = await model.DeleteAsync(Item(sender).Task) ?? "";
    }

    private void StartDrag(object sender, DragStartedEventArgs e)
    {
        if (model?.CanEditTasks != true)
            return;
        dragSource = Item(sender);
        if (ItemsControl.ContainerFromElement(Rows, (DependencyObject)sender) is ListBoxItem container)
        {
            dragContainer = container;
            VirtualizingPanel.SetIsContainerVirtualizable(container, false);
        }
        scrollTimer.Start();
    }

    private void DragTask(object sender, DragDeltaEventArgs e) => UpdateDropTarget();

    private async void FinishDrag(object sender, DragCompletedEventArgs e)
    {
        var from = dragSource;
        var to = dropTarget;
        var after = dropAfter;
        ClearDrag();
        if (e.Canceled || from is null || to is null || model is null)
            return;

        var error = await model.MoveAsync(from.Task.Id, to.Task.Id, after);
        ErrorText.Text = error ?? "";
        if (error is null)
            ShowTask(from.Task.Id);
    }

    private void ScrollWhileDragging(object? sender, EventArgs e)
    {
        if (dragSource is null || Viewer() is not { } scroll)
            return;
        var pointer = Mouse.GetPosition(scroll);
        if (pointer.X >= -16 && pointer.X <= scroll.ActualWidth + 16)
        {
            const double edge = 21;
            var step = pointer.Y < edge ? -12 : pointer.Y > scroll.ActualHeight - edge ? 12 : 0;
            if (step != 0)
            {
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset + step);
                scroll.UpdateLayout();
            }
        }
        UpdateDropTarget();
    }

    private void UpdateDropTarget()
    {
        if (dragSource is null || items.Length == 0 || Rows.ActualHeight < 1 || Rows.ActualWidth < 1)
            return;

        var pointer = Mouse.GetPosition(Rows);
        if (pointer.X < -16 || pointer.X > Rows.ActualWidth + 16)
        {
            MarkDrop(null, false);
            return;
        }

        var y = Math.Clamp(pointer.Y, 1, Math.Max(1, Rows.ActualHeight - 2));
        var container = HitRow(y) ?? HitRow(y - 4) ?? HitRow(y + 4);
        var target = container?.DataContext as Row ?? (y < 5 ? items[0] : items[^1]);
        var after = container is null ? y >= 5 : Mouse.GetPosition(container).Y >= container.ActualHeight / 2;
        if (target.Task.IsCompleted != dragSource.Task.IsCompleted)
        {
            after = !dragSource.Task.IsCompleted;
            target = after ? items.Last(row => !row.Task.IsCompleted) : items.First(row => row.Task.IsCompleted);
        }
        MarkDrop(target == dragSource ? null : target, after);
    }

    private ListBoxItem? HitRow(double y)
    {
        if (Rows.InputHitTest(new Point(Rows.ActualWidth / 2, y)) is not DependencyObject hit)
            return null;
        return ItemsControl.ContainerFromElement(Rows, hit) as ListBoxItem;
    }

    private void MarkDrop(Row? target, bool after)
    {
        if (dropTarget == target && dropAfter == after)
            return;
        dropTarget?.Mark(null);
        dropTarget = target;
        dropAfter = after;
        dropTarget?.Mark(after);
    }

    private void ClearDrag()
    {
        scrollTimer?.Stop();
        if (dragContainer is not null)
        {
            VirtualizingPanel.SetIsContainerVirtualizable(dragContainer, true);
            dragContainer = null;
        }
        dragSource = null;
        MarkDrop(null, false);
    }

    private ScrollViewer? Viewer()
    {
        // A ListBox hosts its viewport directly below its template root.
        return FindViewer(Rows);

        static ScrollViewer? FindViewer(DependencyObject node)
        {
            if (node is ScrollViewer viewer)
                return viewer;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                if (FindViewer(VisualTreeHelper.GetChild(node, i)) is { } child)
                    return child;
            return null;
        }
    }

    private sealed class Row(MemoTask task) : INotifyPropertyChanged
    {
        private bool confirming;
        private bool highlighted;
        public MemoTask Task { get; } = task;
        public event PropertyChangedEventHandler? PropertyChanged;

        public bool DropBefore { get; private set; }
        public bool DropAfter { get; private set; }

        public void Mark(bool? after)
        {
            DropBefore = after == false;
            DropAfter = after == true;
            PropertyChanged?.Invoke(this, new(nameof(DropBefore)));
            PropertyChanged?.Invoke(this, new(nameof(DropAfter)));
        }

        public bool Confirming
        {
            get => confirming;
            set { confirming = value; PropertyChanged?.Invoke(this, new(nameof(Confirming))); }
        }

        public bool Highlighted
        {
            get => highlighted;
            set { highlighted = value; PropertyChanged?.Invoke(this, new(nameof(Highlighted))); }
        }
    }
}
