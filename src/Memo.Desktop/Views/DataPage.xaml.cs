using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Memo.Desktop.Windows;
using Memo.Desktop.ViewModels;

namespace Memo.Desktop.Views;

public partial class DataPage : UserControl
{
    public DataPage() => InitializeComponent();

    private async void ToggleTaskLock(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MemoViewModel model)
            return;
        var locked = !model.TasksLocked;
        var error = await model.SetTaskLockAsync(locked);
        SetFeedback(PermissionStatus, error ?? (locked ? "任务已锁定为只读。" : "任务编辑权限已恢复。"), error is not null);
    }

    private void OpenProperties(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MemoViewModel model || Window.GetWindow(this) is not { } window)
            return;
        try
        {
            Directory.CreateDirectory(model.DirectoryPath);
            FolderProperties.Open(model.DirectoryPath, new WindowInteropHelper(window).Handle);
            SetFeedback(AccessStatus, "已请求打开文件夹属性。请在「安全」页查看或更改权限。", false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
        {
            SetFeedback(AccessStatus, "无法打开文件夹属性。请从资源管理器找到数据目录，再打开其属性。", true);
        }
    }

    private void SetFeedback(TextBlock target, string message, bool error)
    {
        target.Text = message;
        target.SetResourceReference(TextBlock.ForegroundProperty, error ? "Danger" : "Accent");
    }

    private void OpenDirectory(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MemoViewModel model)
            return;
        try
        {
            Directory.CreateDirectory(model.DirectoryPath);
            Process.Start(new ProcessStartInfo(model.DirectoryPath) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
        {
            SetFeedback(AccessStatus, "无法打开存储目录。", true);
        }
    }

    private async void Restore(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MemoViewModel model)
            return;
        var (error, archive) = await model.RestoreAsync();
        var message = error ?? (archive is null ? "已恢复备份。" : $"已恢复备份。原文件保留为 {Path.GetFileName(archive)}。");
        ShowStatus(message, error is not null);
    }

    private void ShowStatus(string message, bool error)
    {
        Status.Text = message;
        Status.SetResourceReference(TextBlock.ForegroundProperty, error ? "Danger" : "Accent");
        StatusBanner.SetResourceReference(Border.BackgroundProperty, error ? "DangerWash" : "Wash");
        StatusBanner.Visibility = Visibility.Visible;
    }
}
