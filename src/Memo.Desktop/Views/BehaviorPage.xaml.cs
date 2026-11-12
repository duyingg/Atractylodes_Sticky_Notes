using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Memo.Desktop.Models;
using Memo.Desktop.ViewModels;
using Memo.Desktop.Windows;

namespace Memo.Desktop.Views;

public partial class BehaviorPage : UserControl
{
    private static readonly PriorityChoice[] Priorities =
    [
        new(PriorityPreset.Low, "低"),
        new(PriorityPreset.BelowNormal, "低于正常（默认）"),
        new(PriorityPreset.Normal, "正常"),
        new(PriorityPreset.AboveNormal, "高于正常"),
        new(PriorityPreset.High, "高")
    ];

    private MemoViewModel? model;
    private PriorityPreset? shownPriority;

    public BehaviorPage()
    {
        InitializeComponent();
        PriorityPicker.ItemsSource = Priorities;
        ProcessDetails.Text = $"进程 {Path.GetFileName(Environment.ProcessPath)} · PID {Environment.ProcessId}";
        DataContextChanged += BindModel;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                ShowPriority(ProcessScheduling.Read(), applied: false);
        };
    }

    internal event Action? PriorityRetryRequested;

    internal void ShowPriority(PriorityStatus status, bool applied)
    {
        CurrentPriority.Text = $"Windows 当前优先级为「{status.Current}」";
        if (applied || status.Error is not null)
            ReportPriority(status.Error ?? "已应用所选优先级。", status.Error is not null);
    }

    private void BindModel(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (model is not null)
            model.PropertyChanged -= ModelChanged;
        model = e.NewValue as MemoViewModel;
        if (model is not null)
        {
            model.PropertyChanged += ModelChanged;
            shownPriority = null;
            Refresh(model.Behavior);
        }
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (model is not null && e.PropertyName == nameof(MemoViewModel.Behavior))
            Refresh(model.Behavior);
    }

    private void Refresh(BehaviorOptions options)
    {
        if (shownPriority != options.Priority)
        {
            shownPriority = options.Priority;
            PriorityPicker.SelectedValue = Enum.IsDefined(options.Priority) ? options.Priority : PriorityPreset.BelowNormal;
        }
    }

    private async void ApplyPriority(object sender, RoutedEventArgs e)
    {
        if (model is null || PriorityPicker.SelectedValue is not PriorityPreset selected)
            return;
        if (model.Behavior.Priority == selected)
        {
            PriorityRetryRequested?.Invoke();
            return;
        }

        var error = await model.SetBehaviorAsync(model.Behavior with { Priority = selected });
        if (error is not null)
            ReportPriority(error, true);
    }

    private void RefreshPriority(object sender, RoutedEventArgs e)
    {
        var status = ProcessScheduling.Read();
        ShowPriority(status, applied: false);
        ReportPriority(status.Error ?? "已从 Windows 读取当前优先级。", status.Error is not null);
    }

    private void OpenTaskManager(object sender, RoutedEventArgs e)
    {
        try
        {
            using var taskManager = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "Taskmgr.exe"))
            {
                UseShellExecute = true
            });
        }
        catch (Win32Exception error)
        {
            ReportPriority($"无法打开任务管理器（Windows 错误 {error.NativeErrorCode}）。", true);
        }
    }

    private void ReportPriority(string message, bool error)
    {
        PriorityFeedback.Text = message;
        PriorityFeedback.SetResourceReference(TextBlock.ForegroundProperty, error ? "Danger" : "Accent");
        PriorityFeedback.Visibility = Visibility.Visible;
    }


    private sealed record PriorityChoice(PriorityPreset Value, string Label);
}
