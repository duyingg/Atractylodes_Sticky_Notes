using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Memo.Desktop.Models;
using Memo.Desktop.ViewModels;
using Memo.Desktop.Windows;

namespace Memo.Desktop.Views;

public partial class BoardWindow : Window
{
    private readonly MemoViewModel model;
    private DesktopLayer? layer;
    private HwndSource? source;
    private BoardGeometry.Point dragStart;
    private BoardGeometry.Rect dragBounds;
    private bool moving;
    private ResizeDrag? resizing;
    private bool editing;

    private sealed record ResizeDrag(BoardGeometry.Placement Placement, BoardGeometry.Point Cursor,
        BoardGeometry.ResizeEdges Edges);

    internal BoardWindow(MemoViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        Tasks.OpenRequested += OpenTask;
        Reader.Closed += EndEdit;
        Form.Cancelled += EndEdit;
        Form.Saved += Saved;
        model.PropertyChanged += OnModelChanged;
    }

    internal event Action? DataRequested;
    internal event Action<BoardGeometry.Placement>? PlacementChanged;
    internal event Action<string, bool>? PositionSaved;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        source = (HwndSource)PresentationSource.FromVisual(this);
        source.AddHook(OnWindowMessage);
        layer = new DesktopLayer(source);
        layer.SetPinned(model.Behavior.AlwaysOnTop);
        ApplyLayout();
        layer.RequestPlacement();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        ApplyLayout();
        layer?.RequestPlacement();
    }

    protected override void OnClosed(EventArgs e)
    {
        model.PropertyChanged -= OnModelChanged;
        layer?.Dispose();
        base.OnClosed(e);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MemoViewModel.Layout))
            ApplyLayout();
        if (e.PropertyName == nameof(MemoViewModel.Behavior))
            layer?.SetPinned(model.Behavior.AlwaysOnTop);
    }

    internal BoardGeometry.Placement? Placement => source is { IsDisposed: false }
        ? BoardGeometry.CurrentPlacement(source.Handle) : null;

    private nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is 0x001A or 0x007E or 0x02E0) // settings, display or DPI changed
            Dispatcher.BeginInvoke(DispatcherPriority.Background, ApplyLayout);
        return 0;
    }

    private void ApplyLayout()
    {
        if (source is not { IsDisposed: false } || moving || resizing is not null)
            return;
        BoardGeometry.Apply(source.Handle, model.Layout);
        if (BoardGeometry.CurrentPlacement(source.Handle) is { } placement)
            PlacementChanged?.Invoke(placement);
    }

    private void Add(object sender, RoutedEventArgs e)
    {
        BeginEdit(null);
    }

    internal void Wake() => layer?.Wake();

    internal void QuickAdd()
    {
        if (model.TaskReadOnly)
        {
            DataRequested?.Invoke();
            return;
        }

        Wake();
        if (editing)
        {
            Activate();
            Form.FocusInput();
        }
        else
        {
            BeginEdit(null);
        }
    }

    internal void ShowError(string message) => Notice.Text = message;

    private void OpenData(object sender, RoutedEventArgs e) => DataRequested?.Invoke();

    private void BeginEdit(MemoTask? task)
    {
        if (!model.CanEditTasks || editing || layer is null)
            return;
        editing = true;
        Focusable = true;
        ListPage.Visibility = Visibility.Collapsed;
        Reader.Visibility = Visibility.Collapsed;
        Form.Visibility = Visibility.Visible;
        layer.BeginInput();
        layer.Wake();
        Activate();
        Form.Begin(task);
    }

    private void OpenTask(MemoTask task)
    {
        if (model.CanEditTasks)
        {
            BeginEdit(task);
            return;
        }
        if (editing || layer is null)
            return;
        Focusable = true;
        ListPage.Visibility = Visibility.Collapsed;
        Reader.Visibility = Visibility.Visible;
        layer.BeginInput();
        layer.Wake();
        Activate();
        Reader.ShowTask(task);
    }

    private void EndEdit()
    {
        editing = false;
        Focusable = false;
        Form.Visibility = Visibility.Collapsed;
        Reader.Visibility = Visibility.Collapsed;
        ListPage.Visibility = Visibility.Visible;
        layer?.EndInput();
    }

    private void Saved(Guid id, bool _)
    {
        EndEdit();
        Tasks.ShowTask(id);
    }

    private void PreventFocus(object sender, KeyboardFocusChangedEventArgs e) => e.Handled = !editing && Reader?.Visibility != Visibility.Visible;

    private void StartResize(object sender, DragStartedEventArgs e)
    {
        if (!model.CanEdit || source is null || BoardGeometry.CurrentPlacement(source.Handle) is not { } placement
            || sender is not Thumb { Tag: string tag })
            return;

        resizing = new ResizeDrag(placement, BoardGeometry.Cursor(), Enum.Parse<BoardGeometry.ResizeEdges>(tag));
    }

    private void ResizeBoard(object sender, DragDeltaEventArgs e)
    {
        if (resizing is not { } start || source is null)
            return;

        BoardGeometry.SetBounds(source.Handle, ResizeBounds(start), start.Placement.Scale);
    }

    private async void FinishResize(object sender, DragCompletedEventArgs e)
    {
        if (resizing is not { } start || source is null)
            return;
        resizing = null;
        var original = start.Placement.Board;
        var bounds = ResizeBounds(start);
        if (!e.Canceled && (bounds.Width != original.Width || bounds.Height != original.Height))
        {
            var layout = model.Layout;
            var scale = start.Placement.Scale;
            var width = bounds.Width == original.Width ? layout.Edge : LogicalSize(bounds.Width, scale);
            var height = bounds.Height == original.Height ? layout.EffectiveHeight : LogicalSize(bounds.Height, scale);
            BoardGeometry.SetBounds(source.Handle, bounds, scale);
            var error = await model.SetLayoutAsync(new BoardLayout(width, bounds.Left, bounds.Top,
                height == width ? null : height));
            Notice.Text = error ?? "";
            PositionSaved?.Invoke(error ?? "便签大小已保存。", error is not null);
        }
        ApplyLayout();
    }

    private static double LogicalSize(int pixels, double scale) =>
        Math.Clamp(pixels / scale, BoardLayout.MinimumEdge, BoardLayout.MaximumEdge);

    private static BoardGeometry.Rect ResizeBounds(ResizeDrag start)
    {
        var cursor = BoardGeometry.Cursor();
        return BoardGeometry.Resize(start.Placement, start.Edges,
            new BoardGeometry.Point(cursor.X - start.Cursor.X, cursor.Y - start.Cursor.Y));
    }

    private void StartMove(object sender, DragStartedEventArgs e)
    {
        if (!model.CanEdit || source is null)
            return;
        dragStart = BoardGeometry.Cursor();
        dragBounds = BoardGeometry.Bounds(source.Handle);
        moving = true;
    }

    private void MoveBoard(object sender, DragDeltaEventArgs e)
    {
        if (!moving || source is null)
            return;

        var cursor = BoardGeometry.Cursor();
        BoardGeometry.Move(source.Handle, dragBounds.Left + cursor.X - dragStart.X,
            dragBounds.Top + cursor.Y - dragStart.Y);
    }

    private async void FinishMove(object sender, DragCompletedEventArgs e)
    {
        if (!moving || source is null)
            return;

        moving = false;
        var box = BoardGeometry.Bounds(source.Handle);
        if (!e.Canceled && (box.Left != dragBounds.Left || box.Top != dragBounds.Top))
        {
            var error = await model.SetLayoutAsync(model.Layout with { X = box.Left, Y = box.Top });
            Notice.Text = error ?? "";
            PositionSaved?.Invoke(error ?? "位置已保存。", error is not null);
        }
        ApplyLayout();
    }
}
