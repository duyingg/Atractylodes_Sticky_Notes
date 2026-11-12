using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Memo.Desktop.ViewModels;
using Memo.Desktop.Windows;

namespace Memo.Desktop.Views;

public partial class SettingsWindow : Window
{
    private bool exiting;

    internal SettingsWindow(MemoViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        Width = Math.Min(Width, area.Width);
        Height = Math.Min(Height, area.Height);
        TasksNav.IsChecked = true;
        TasksPage.PermissionsRequested += OpenData;
    }

    public event EventHandler? ExitRequested;
    internal event Action? WakeRequested;
    internal BehaviorPage BehaviorControls => BehaviorPage;
    internal ShortcutPage ShortcutControls => ShortcutPage;
    internal void Open()
    {
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    internal void OpenData()
    {
        DataNav.IsChecked = true;
        Open();
    }

    internal void QuickAddTask()
    {
        TasksNav.IsChecked = true;
        Open();
        TasksPage.QuickAdd();
    }

    internal void UpdatePlacement(BoardGeometry.Placement placement) => LayoutPage.UpdatePlacement(placement);
    internal void PositionSaved(string message, bool error) => LayoutPage.ShowStatus(message, error);
    internal void PermitClose() => exiting = true;

    private void Navigate(object sender, RoutedEventArgs e)
    {
        var page = ((RadioButton)sender).Tag as string;
        TasksPage.Visibility = page == "Tasks" ? Visibility.Visible : Visibility.Collapsed;
        SchedulePage.Visibility = page == "Schedule" ? Visibility.Visible : Visibility.Collapsed;
        LayoutPage.Visibility = page == "Layout" ? Visibility.Visible : Visibility.Collapsed;
        BehaviorPage.Visibility = page == "Behavior" ? Visibility.Visible : Visibility.Collapsed;
        ShortcutPage.Visibility = page == "Shortcuts" ? Visibility.Visible : Visibility.Collapsed;
        PanelSettings.Visibility = page == "Panels" ? Visibility.Visible : Visibility.Collapsed;
        SkinSettings.Visibility = page == "Skin" ? Visibility.Visible : Visibility.Collapsed;
        DataPage.Visibility = page == "Data" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Minimize(object sender, RoutedEventArgs e) => Hide();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!exiting)
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(Hide);
        }
    }

    private void ExitClicked(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);

    private void WakeClicked(object sender, RoutedEventArgs e) => WakeRequested?.Invoke();
}
