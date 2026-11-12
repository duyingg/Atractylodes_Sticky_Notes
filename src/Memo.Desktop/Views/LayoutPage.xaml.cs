using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using Memo.Desktop.Models;
using Memo.Desktop.ViewModels;
using Memo.Desktop.Windows;

namespace Memo.Desktop.Views;

public partial class LayoutPage : UserControl
{
    private BoardGeometry.Placement? placement;
    private MemoViewModel? model;
    private (double Width, double Height)? shownSize;
    private int appliedFontSize;
    private int draftFontSize;

    public LayoutPage()
    {
        InitializeComponent();
        DataContextChanged += BindModel;
        UpdateFontPreview(DisplayOptions.Default.FontSize);
    }

    private void BindModel(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (model is not null)
            model.PropertyChanged -= ModelChanged;
        model = e.NewValue as MemoViewModel;
        if (model is null)
            return;
        model.PropertyChanged += ModelChanged;
        shownSize = null;
        appliedFontSize = model.Display.FontSize;
        UpdateFontPreview(appliedFontSize);
        RefreshSize();
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MemoViewModel.Layout))
            RefreshSize();
        if (e.PropertyName == nameof(MemoViewModel.Display) && model is not null && model.Display.FontSize != appliedFontSize)
        {
            appliedFontSize = model.Display.FontSize;
            UpdateFontPreview(appliedFontSize);
        }
    }

    private void RefreshSize()
    {
        if (model is null)
            return;
        var size = (Width: model.Layout.Edge, Height: model.Layout.EffectiveHeight);
        if (shownSize == size)
            return;
        shownSize = size;
        WidthInput.Text = size.Width.ToString("0");
        HeightInput.Text = size.Height.ToString("0");
        AppliedSize.Text = $"当前已应用 {size.Width:0} × {size.Height:0}";
    }

    internal void UpdatePlacement(BoardGeometry.Placement current)
    {
        placement = current;
        MonitorTitle.Text = current.IsPrimary ? "主显示器  ·  工作区预览" : "副显示器  ·  工作区预览";
        Coordinates.Text = $"位置 {current.Board.Left}, {current.Board.Top} px  ·  显示缩放 {current.Dpi / 96d:P0}";
        DrawPreview();
    }

    internal void ShowStatus(string message, bool error)
    {
        StatusBanner.SetResourceReference(Border.BackgroundProperty, error ? "DangerWash" : "Wash");
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, error ? "Danger" : "Accent");
        StatusText.Text = message;
        StatusBanner.Visibility = Visibility.Visible;
    }

    private void RefreshPreview(object sender, SizeChangedEventArgs e) => DrawPreview();

    private void DrawPreview()
    {
        Preview.Children.Clear();
        if (placement is not { } current || Preview.ActualWidth <= 0 || Preview.ActualHeight <= 0)
            return;
        var monitor = current.Monitor;
        var width = monitor.Right - monitor.Left;
        var height = monitor.Bottom - monitor.Top;
        if (width <= 0 || height <= 0)
            return;
        var scale = Math.Min(Preview.ActualWidth / width, Preview.ActualHeight / height);
        var x = (Preview.ActualWidth - width * scale) / 2;
        var y = (Preview.ActualHeight - height * scale) / 2;

        void Draw(BoardGeometry.Rect bounds, string fill, string? stroke = null)
        {
            var rect = new Rectangle
            {
                Width = Math.Max(0, (bounds.Right - bounds.Left) * scale),
                Height = Math.Max(0, (bounds.Bottom - bounds.Top) * scale),
                StrokeThickness = 1.5,
                RadiusX = 3,
                RadiusY = 3
            };
            rect.SetResourceReference(Shape.FillProperty, fill);
            if (stroke is not null)
                rect.SetResourceReference(Shape.StrokeProperty, stroke);
            Canvas.SetLeft(rect, x + (bounds.Left - monitor.Left) * scale);
            Canvas.SetTop(rect, y + (bounds.Top - monitor.Top) * scale);
            Preview.Children.Add(rect);
        }

        Draw(monitor, "MonitorFill");
        Draw(current.Work, "WorkFill");
        Draw(current.Board, "Surface", "Accent");
    }

    private async void ApplySize(object sender, RoutedEventArgs e)
    {
        if (model is null)
            return;
        if (!int.TryParse(WidthInput.Text, out var width) || width is < BoardLayout.MinimumEdge or > BoardLayout.MaximumEdge
            || !int.TryParse(HeightInput.Text, out var height) || height is < BoardLayout.MinimumEdge or > BoardLayout.MaximumEdge)
        {
            ShowStatus("宽度和高度请输入 240 至 640 之间的整数。", true);
            return;
        }

        var error = await model.SetLayoutAsync(model.Layout with { Edge = width, Height = height == width ? null : height });
        ShowStatus(error ?? "大小已保存。", error is not null);
    }

    private async void ResetPosition(object sender, RoutedEventArgs e)
    {
        if (model is null)
            return;
        var error = await model.SetLayoutAsync(model.Layout with { X = null, Y = null });
        ShowStatus(error ?? "已恢复默认位置。", error is not null);
    }

    private void DecreaseFont(object sender, RoutedEventArgs e) => UpdateFontPreview(draftFontSize - 1);

    private void IncreaseFont(object sender, RoutedEventArgs e) => UpdateFontPreview(draftFontSize + 1);

    private void UpdateFontPreview(int size)
    {
        draftFontSize = Math.Clamp(size, DisplayOptions.MinimumFontSize, DisplayOptions.MaximumFontSize);
        FontValue.Text = draftFontSize.ToString();
        FontPreview.FontSize = draftFontSize;
        FontDown.IsEnabled = draftFontSize > DisplayOptions.MinimumFontSize;
        FontUp.IsEnabled = draftFontSize < DisplayOptions.MaximumFontSize;
    }

    private async void ApplyFont(object sender, RoutedEventArgs e)
    {
        if (model is null)
            return;
        var error = await model.SetDisplayAsync(model.Display with { FontSize = draftFontSize });
        ShowStatus(error ?? "字号已保存。", error is not null);
    }

    private async void SetAddPosition(object sender, RoutedEventArgs e)
    {
        if (model is null)
            return;
        var error = await model.SetDisplayAsync(model.Display with { AddAtTop = AddAtTop.IsChecked == true });
        if (error is not null)
            AddAtTop.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
        ShowStatus(error ?? "新增按钮位置已保存。", error is not null);
    }
}
