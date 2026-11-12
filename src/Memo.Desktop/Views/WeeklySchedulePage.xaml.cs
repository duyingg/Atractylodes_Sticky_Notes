using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Memo.Desktop.Models;
using Memo.Desktop.ViewModels;

namespace Memo.Desktop.Views;

public partial class WeeklySchedulePage : UserControl
{
    private sealed record Weekday(DayOfWeek Day, string Label);

    private static readonly Weekday[] Week =
    [
        new(DayOfWeek.Monday, "周一"), new(DayOfWeek.Tuesday, "周二"), new(DayOfWeek.Wednesday, "周三"),
        new(DayOfWeek.Thursday, "周四"), new(DayOfWeek.Friday, "周五"), new(DayOfWeek.Saturday, "周六"),
        new(DayOfWeek.Sunday, "周日")
    ];

    private MemoViewModel? model;

    public WeeklySchedulePage()
    {
        InitializeComponent();
        DataContextChanged += BindModel;
        Days.ItemsSource = Week;
        Days.SelectedIndex = Array.FindIndex(Week, weekday => weekday.Day == DateTime.Today.DayOfWeek);
    }

    private void BindModel(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (model is not null)
            model.PropertyChanged -= OnModelChanged;
        model = e.NewValue as MemoViewModel;
        if (model is not null)
            model.PropertyChanged += OnModelChanged;
        RefreshEntries();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MemoViewModel.WeeklySchedule))
            RefreshEntries();
    }

    private void RefreshEntries()
    {
        if (Days.SelectedItem is not Weekday day)
            return;
        var selected = (Entries.SelectedItem as WeeklyTask)?.Id;
        var entries = (model?.WeeklySchedule ?? []).Where(item => item.Day == day.Day).ToArray();
        Entries.ItemsSource = entries;
        Entries.SelectedItem = entries.FirstOrDefault(item => item.Id == selected);
    }

    private void DayChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != Days)
            return;
        ClearEditor();
        RefreshEntries();
        Status.Text = "";
    }

    private void EntryChanged(object sender, SelectionChangedEventArgs e)
    {
        var entry = Entries.SelectedItem as WeeklyTask;
        Input.Text = entry?.Text ?? "";
        DeleteButton.IsEnabled = entry is not null;
    }

    private void ClearEditor()
    {
        Entries.SelectedItem = null;
        Input.Clear();
    }

    private void NewEntry(object sender, RoutedEventArgs e)
    {
        ClearEditor();
        Status.Text = "";
        Input.Focus();
    }

    private async void SaveEntry(object sender, RoutedEventArgs e)
    {
        if (model is not { CanEditTasks: true } || Days.SelectedItem is not Weekday day)
            return;
        var selected = Entries.SelectedItem as WeeklyTask;
        var error = await model.SaveWeeklyTaskAsync(selected?.Id, day.Day, Input.Text, DateOnly.FromDateTime(DateTime.Now));
        if (error is null && selected is null && Days.SelectedItem is Weekday current
            && current.Day == day.Day && Entries.SelectedItem is null)
            Input.Clear();
        Report(error, "已保存默认待办。");
    }

    private async void DeleteEntry(object sender, RoutedEventArgs e)
    {
        if (model is not { CanEditTasks: true } || Entries.SelectedItem is not WeeklyTask entry
            || MessageBox.Show(Window.GetWindow(this), "删除这项默认待办？", "日程表", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        var error = await model.DeleteWeeklyTaskAsync(entry.Id, DateOnly.FromDateTime(DateTime.Now));
        Report(error, "已删除默认待办。");
    }

    private void Report(string? error, string success)
    {
        Status.SetResourceReference(TextBlock.ForegroundProperty, error is null ? "Accent" : "Danger");
        Status.Text = error ?? success;
    }
}
