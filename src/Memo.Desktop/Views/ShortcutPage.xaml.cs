using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Memo.Desktop.Models;
using Memo.Desktop.ViewModels;

namespace Memo.Desktop.Views;

public partial class ShortcutPage : UserControl
{
    private readonly ShortcutRow[] rows =
    [
        new(ShortcutAction.Wake, "唤醒便签"),
        new(ShortcutAction.Pin, "切换置顶"),
        new(ShortcutAction.Add, "快速新增任务"),
        new(ShortcutAction.Settings, "打开设置")
    ];
    private MemoViewModel? model;
    private ShortcutKeys? shownKeys;
    private bool shownEnabled;

    public ShortcutPage()
    {
        InitializeComponent();
        Entries.ItemsSource = rows;
        DataContextChanged += BindModel;
    }

    internal event Action? ShortcutsSaved;

    internal void UpdateShortcuts(IReadOnlyDictionary<ShortcutAction, string> statuses)
    {
        foreach (var row in rows)
            if (statuses.TryGetValue(row.Action, out var status))
                row.Status = status;
    }

    private void BindModel(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (model is not null)
            model.PropertyChanged -= ModelChanged;
        model = e.NewValue as MemoViewModel;
        if (model is not null)
        {
            model.PropertyChanged += ModelChanged;
            shownKeys = null;
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
        if (shownKeys == options.EffectiveKeys && shownEnabled == options.ShortcutsEnabled)
            return;

        shownKeys = options.EffectiveKeys;
        shownEnabled = options.ShortcutsEnabled;
        FillShortcuts(shownKeys, shownEnabled);
    }

    private void FillShortcuts(ShortcutKeys keys, bool enabled)
    {
        foreach (var row in rows)
            row.Gesture = keys.Get(row.Action) ?? "";
        EnableShortcuts.IsChecked = enabled;
    }

    private async void Apply(object sender, RoutedEventArgs e)
    {
        var normalized = new string[rows.Length];
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < rows.Length; i++)
        {
            if (!ShortcutChord.TryParse(rows[i].Gesture, out var chord))
            {
                Report($"「{rows[i].Title}」的快捷键无效。请搭配至少两个修饰键。", true);
                return;
            }
            if (!used.Add(chord.Text))
            {
                Report($"组合 {chord.Text} 重复，请为每个操作使用不同组合。", true);
                return;
            }
            normalized[i] = chord.Text;
        }

        await SaveShortcuts(new ShortcutKeys(normalized[0], normalized[1], normalized[2], normalized[3]),
            EnableShortcuts.IsChecked == true);
    }

    private async void Reset(object sender, RoutedEventArgs e) => await SaveShortcuts(ShortcutKeys.Default, true);

    private async Task SaveShortcuts(ShortcutKeys keys, bool enabled)
    {
        if (model is null)
            return;

        var current = model.Behavior;
        var next = current with { Keys = keys, ShortcutsEnabled = enabled };
        var error = await model.SetBehaviorAsync(next);
        if (error is not null)
        {
            Report(error, true);
            return;
        }

        FillShortcuts(keys, enabled);
        if (current == next)
            ShortcutsSaved?.Invoke();

        var failed = enabled && rows.Any(row => row.Status != "已生效");
        Report(failed ? "配置已保存。有快捷键未注册成功，请更换组合后再应用。" : "快捷键设置已应用。", failed);
    }

    private void Report(string message, bool error)
    {
        Feedback.SetResourceReference(TextBlock.ForegroundProperty, error ? "Danger" : "Accent");
        Feedback.Text = message;
        Feedback.Visibility = Visibility.Visible;
    }

    private sealed class ShortcutRow(ShortcutAction action, string title) : INotifyPropertyChanged
    {
        private string gesture = "";
        private string status = "等待注册";
        public ShortcutAction Action { get; } = action;
        public string Title { get; } = title;
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Gesture
        {
            get => gesture;
            set { gesture = value; PropertyChanged?.Invoke(this, new(nameof(Gesture))); }
        }
        public string Status
        {
            get => status;
            set { status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); }
        }
    }
}
