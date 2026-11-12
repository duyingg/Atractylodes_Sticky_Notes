using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shapes;
using System.Windows.Threading;
using Memo.Desktop.Models;
using Memo.Desktop.ViewModels;
using Memo.Desktop.Windows;

namespace Memo.Desktop.Views;

public partial class NoteWindow : Window
{
    private readonly MemoViewModel model;
    private readonly DispatcherTimer saveDelay;
    private HwndSource? source;
    private DesktopLayer? layer;
    private BoardGeometry.Placement? neighbor;
    private string committedText;
    private Task<string?>? writing;
    private bool rulingQueued;

    internal NoteWindow(MemoViewModel model)
    {
        this.model = model;
        committedText = model.Note;
        saveDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        saveDelay.Tick += SaveAfterPause;
        InitializeComponent();
        DataContext = model;
        Editor.Text = committedText;
        model.PropertyChanged += ModelChanged;
        Editor.Loaded += (_, _) => QueueRuling();
        Editor.SizeChanged += (_, _) => QueueRuling();
        Editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => QueueRuling()));
    }

    internal void SetPanelMenu(Action close, Action exit)
    {
        ContextMenu = PanelContextMenu.Create(close, exit, Editor);
        Editor.ContextMenu = ContextMenu;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        source = (HwndSource)PresentationSource.FromVisual(this);
        source.AddHook(WindowMessage);
        layer = new DesktopLayer(source);
        layer.SetPinned(model.Behavior.AlwaysOnTop);
        ApplyLayout();
        layer.RequestPlacement();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        layer?.EndInput();
    }

    protected override void OnClosed(EventArgs e)
    {
        saveDelay.Stop();
        model.PropertyChanged -= ModelChanged;
        layer?.Dispose();
        base.OnClosed(e);
    }

    internal void PlaceBeside(BoardGeometry.Placement? placement)
    {
        neighbor = placement;
        ApplyLayout();
    }

    internal void Wake() => layer?.Wake();

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is 0x001A or 0x007E or 0x02E0)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, ApplyLayout);
        return 0;
    }

    private void ApplyLayout()
    {
        if (source is not { IsDisposed: false })
            return;
        var layout = neighbor is { } placement ? BoardGeometry.AdjacentNoteLayout(placement) : new BoardLayout();
        BoardGeometry.Apply(source.Handle, layout);
    }

    private void BeginTyping(object sender, MouseButtonEventArgs e)
    {
        if (IsActive || layer is null)
            return;
        layer.BeginInput();
        if (!Activate())
            layer.EndInput();
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MemoViewModel.Behavior))
            layer?.SetPinned(model.Behavior.AlwaysOnTop);
        if (e.PropertyName == nameof(MemoViewModel.Display))
            QueueRuling();
        if (e.PropertyName != nameof(MemoViewModel.Note))
            return;

        var followsStore = writing is not { IsCompleted: false } && Editor.Text == committedText;
        committedText = model.Note;
        if (followsStore)
            Editor.Text = committedText;
    }

    private void DraftChanged(object sender, TextChangedEventArgs e)
    {
        saveDelay.Stop();
        if (Editor is not null && Editor.Text != model.Note)
            saveDelay.Start();
        QueueRuling();
    }

    private void QueueRuling()
    {
        if (rulingQueued)
            return;
        rulingQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            rulingQueued = false;
            DrawRuling();
        });
    }

    private void DrawRuling()
    {
        Ruling.Children.Clear();
        if (!model.Display.NoteLines || Ruling.ActualHeight <= 0 || Editor.ViewportWidth <= 0)
            return;

        var left = Editor.Padding.Left;
        var right = Math.Min(left + Editor.ViewportWidth, Ruling.ActualWidth - Editor.Padding.Right);
        var bottom = Math.Min(Ruling.ActualHeight - Editor.Padding.Bottom, Editor.Padding.Top + Editor.ViewportHeight);
        if (right <= left || bottom <= Editor.Padding.Top)
            return;

        void DrawLine(double y)
        {
            if (y < Editor.Padding.Top || y > bottom)
                return;
            var rule = new Line { X1 = left, X2 = right, Y1 = y, Y2 = y, StrokeThickness = 1 };
            rule.SetResourceReference(Shape.StrokeProperty, "NoteRule");
            Ruling.Children.Add(rule);
        }

        var stride = Editor.FontFamily.LineSpacing * Editor.FontSize;
        var nextY = Editor.Padding.Top + stride - 1;
        var first = Editor.GetFirstVisibleLineIndex();
        var last = Editor.GetLastVisibleLineIndex();
        for (var line = first; line >= 0 && line <= last; line++)
        {
            var index = Editor.GetCharacterIndexFromLineIndex(line);
            if (index < 0)
                continue;
            var bounds = Editor.GetRectFromCharacterIndex(index);
            if (bounds.IsEmpty || bounds.Height <= 0)
                continue;
            stride = bounds.Height;
            DrawLine(bounds.Bottom - 1);
            nextY = bounds.Bottom + stride - 1;
        }
        for (var y = nextY; y <= bottom; y += stride)
            DrawLine(y);
    }

    private async void SaveAfterPause(object? sender, EventArgs e)
    {
        saveDelay.Stop();
        var error = await SaveDraftAsync();
        Frame.SetResourceReference(Border.BorderBrushProperty, error is null ? "Edge" : "Danger");
        Editor.ToolTip = error;
        if (error is null && Editor.Text != model.Note && !saveDelay.IsEnabled)
            saveDelay.Start();
    }

    internal async Task<string?> FlushAsync()
    {
        saveDelay.Stop();
        while (writing is { IsCompleted: false } || Editor.Text != model.Note)
        {
            var error = await SaveDraftAsync();
            if (error is not null)
                return error;
        }
        return null;
    }

    private async Task<string?> SaveDraftAsync()
    {
        if (!model.CanEdit && writing is not { IsCompleted: false })
            await model.WaitForSaveAsync();
        if (writing is { IsCompleted: false } pending)
            return await pending;
        if (Editor.Text == model.Note)
            return null;

        writing = model.SetNoteAsync(Editor.Text);
        return await writing;
    }
}
