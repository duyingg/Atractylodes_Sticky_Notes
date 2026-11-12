using System.ComponentModel;
using System.IO;
using System.Text.Json;
using Memo.Desktop.Models;
using Memo.Desktop.Services;

namespace Memo.Desktop.ViewModels;

internal sealed class MemoViewModel(JsonMemoStore store, MemoLoadResult initial) : INotifyPropertyChanged
{
    private MemoDocument document = initial.Document.WithGroupedTasks();
    private string? loadWarning = initial.Warning;
    private BackupStatus backup = initial.Backup;
    private Task pendingSave = Task.CompletedTask;
    private bool busy;
    public event PropertyChangedEventHandler? PropertyChanged;

    public MemoTask[] Tasks => document.Tasks;
    public WeeklyTask[] WeeklySchedule => document.WeeklySchedule ?? [];
    public BoardLayout Layout => document.Layout;
    public BehaviorOptions Behavior => document.Behavior ?? BehaviorOptions.Default;
    public DisplayOptions Display => document.Display ?? DisplayOptions.Default;
    public string Note => document.Note ?? "";
    public double TaskLineHeight => Display.FontSize + 6;
    public double TaskPreviewHeight => 2 * TaskLineHeight + 1;
    public string DirectoryPath => store.DirectoryPath;
    public string DataPath => store.Path;
    public string? LoadWarning => loadWarning;
    public bool HasWarning => loadWarning is not null;
    public bool IsUnavailable => HasWarning && backup != BackupStatus.Available;
    public bool CanEdit => !HasWarning && !busy;
    public bool CanEditTasks => CanEdit && !document.TasksLocked;
    public bool TasksLocked => document.TasksLocked;
    public bool TaskReadOnly => HasWarning || TasksLocked;
    public string TaskPermissionAction => TasksLocked ? "解除任务编辑锁" : "锁定任务编辑";
    public string TaskPermissionHint => HasWarning ? "数据处于只读状态。" : "任务编辑已锁定。";
    public bool ShowRestore => HasWarning && backup == BackupStatus.Available;
    public bool CanRestore => ShowRestore && !busy;
    public string BackupLabel => backup switch
    {
        BackupStatus.Available => "可用",
        BackupStatus.Missing => "尚未生成",
        _ => "不可用"
    };
    public int Remaining => Tasks.Count(task => !task.IsCompleted);
    public int Completed => Tasks.Length - Remaining;

    public Task<string?> SaveTaskAsync(MemoTask? task, string text)
    {
        text = text.Trim();
        if (text.Length == 0 || text.Length > MemoDocument.MaximumTaskLength)
            return Task.FromResult<string?>("请输入 1 至 240 个字符。");
        if (task is not null && !Tasks.Any(item => item.Id == task.Id))
            return Task.FromResult<string?>("这项任务已删除。");

        MemoTask[] updated = task is null
            ? [.. Tasks, new MemoTask(Guid.NewGuid(), text)]
            : Tasks.Select(item => item.Id == task.Id ? item with { Text = text } : item).ToArray();
        return SaveTaskChangeAsync((document with { Tasks = updated }).WithGroupedTasks());
    }

    public Task<string?> ToggleAsync(MemoTask task)
    {
        var current = Tasks.FirstOrDefault(item => item.Id == task.Id);
        if (current is null)
            return Task.FromResult<string?>("这项任务已删除。");

        MemoTask[] updated = [.. Tasks.Where(item => item.Id != task.Id), current with { IsCompleted = !current.IsCompleted }];
        return SaveTaskChangeAsync((document with { Tasks = updated }).WithGroupedTasks());
    }

    public Task<string?> MoveAsync(Guid sourceId, Guid targetId, bool after)
    {
        var source = Tasks.FirstOrDefault(task => task.Id == sourceId);
        var target = Tasks.FirstOrDefault(task => task.Id == targetId);
        if (source is null || target is null || source == target)
            return Task.FromResult<string?>(null);

        var updated = Tasks.ToList();
        updated.Remove(source);
        var index = source.SortGroup == target.SortGroup
            ? updated.IndexOf(target) + (after ? 1 : 0)
            : updated.Count(task => task.SortGroup < source.SortGroup
                || (task.SortGroup == source.SortGroup && target.SortGroup > source.SortGroup));
        updated.Insert(index, source);
        return updated.SequenceEqual(Tasks)
            ? Task.FromResult<string?>(null)
            : SaveTaskChangeAsync(document with { Tasks = [.. updated] });
    }

    public Task<string?> SetBehaviorAsync(BehaviorOptions options) =>
        SaveAsync(document with { Behavior = options });

    public Task<string?> DeleteAsync(MemoTask task) =>
        SaveTaskChangeAsync(document with { Tasks = Tasks.Where(item => item.Id != task.Id).ToArray() });

    public Task<string?> SaveWeeklyTaskAsync(Guid? id, DayOfWeek day, string text, DateOnly today)
    {
        text = text.Trim();
        if (text.Length == 0 || text.Length > MemoDocument.MaximumTaskLength)
            return Task.FromResult<string?>("请输入 1 至 240 个字符。");
        if (!Enum.IsDefined(day))
            return Task.FromResult<string?>("请选择星期。");
        if (id is not null && !WeeklySchedule.Any(item => item.Id == id))
            return Task.FromResult<string?>("这项默认待办已删除。");

        WeeklyTask[] updated = id is null
            ? [.. WeeklySchedule, new WeeklyTask(Guid.NewGuid(), day, text)]
            : WeeklySchedule.Select(item => item.Id == id ? item with { Day = day, Text = text } : item).ToArray();
        return SaveWeeklyScheduleAsync(updated, today);
    }

    public Task<string?> DeleteWeeklyTaskAsync(Guid id, DateOnly today) =>
        SaveWeeklyScheduleAsync(WeeklySchedule.Where(item => item.Id != id).ToArray(), today);

    public Task<string?> RefreshDailyTasksAsync(DateOnly today) =>
        !CanEdit || WeeklySchedule.Length == 0 || document.ScheduleDate == today
            ? Task.FromResult<string?>(null)
            : SaveAsync(document.WithDailyTasks(today));

    private Task<string?> SaveWeeklyScheduleAsync(WeeklyTask[] updated, DateOnly today) =>
        SaveTaskChangeAsync((document with { WeeklySchedule = updated }).WithDailyTasks(today, WeeklySchedule));

    public Task<string?> SetLayoutAsync(BoardLayout layout) => SaveAsync(document with { Layout = layout });

    public Task<string?> SetDisplayAsync(DisplayOptions options)
    {
        if (!options.HasVisiblePanel)
            return Task.FromResult<string?>("请至少选择一个栏目。");
        if (options.FontSize is < DisplayOptions.MinimumFontSize or > DisplayOptions.MaximumFontSize)
            return Task.FromResult<string?>($"字号请输入 {DisplayOptions.MinimumFontSize} 至 {DisplayOptions.MaximumFontSize} 之间的整数。");
        return SaveAsync(document with { Display = options });
    }

    public Task<string?> SetNoteAsync(string text) =>
        text == Note ? Task.FromResult<string?>(null) : SaveAsync(document with { Note = text });

    public Task<string?> SetTaskLockAsync(bool locked) => SaveAsync(document with { TasksLocked = locked });

    private Task<string?> SaveTaskChangeAsync(MemoDocument next) =>
        TasksLocked ? Task.FromResult<string?>("任务编辑已锁定。请在「数据与权限」解锁后重试。") : SaveAsync(next);

    public Task WaitForSaveAsync() => pendingSave;

    public Task<(string? Error, string? Archive)> RestoreAsync()
    {
        if (!CanRestore)
            return Task.FromResult<(string?, string?)>(("当前没有可恢复的备份。", null));
        var operation = RestoreCoreAsync();
        pendingSave = operation;
        return operation;
    }

    private async Task<(string? Error, string? Archive)> RestoreCoreAsync()
    {
        SetBusy(true);
        try
        {
            var (restored, archive) = await Task.Run(store.Restore);
            loadWarning = null;
            ReplaceDocument(restored.WithGroupedTasks());
            NotifyData();
            return (null, archive);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return ("恢复失败。请检查存储目录与备份文件后重试。", null);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private Task<string?> SaveAsync(MemoDocument next)
    {
        if (!CanEdit)
            return Task.FromResult<string?>(LoadWarning ?? "正在保存，请稍后重试。");
        if (next == document)
            return Task.FromResult<string?>(null);

        var saving = CommitAsync(next);
        pendingSave = saving;
        return saving;
    }

    private async Task<string?> CommitAsync(MemoDocument next)
    {
        SetBusy(true);
        try
        {
            backup = await Task.Run(() => store.Save(next));
            ReplaceDocument(next);
            NotifyData();
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return "保存失败，请检查存储目录后重试。";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ReplaceDocument(MemoDocument next)
    {
        var previous = document;
        document = next;
        if (previous.Tasks != next.Tasks)
        {
            Changed(nameof(Tasks));
            Changed(nameof(Remaining));
            Changed(nameof(Completed));
        }
        if (previous.Layout != next.Layout)
            Changed(nameof(Layout));
        if (previous.WeeklySchedule != next.WeeklySchedule)
            Changed(nameof(WeeklySchedule));
        if (previous.Behavior != next.Behavior)
            Changed(nameof(Behavior));
        if (previous.Display != next.Display)
        {
            Changed(nameof(Display));
            Changed(nameof(TaskLineHeight));
            Changed(nameof(TaskPreviewHeight));
        }
        if (previous.Note != next.Note)
            Changed(nameof(Note));
    }

    private void NotifyData()
    {
        Changed(nameof(TasksLocked));
        Changed(nameof(TaskReadOnly));
        Changed(nameof(CanEditTasks));
        Changed(nameof(TaskPermissionAction));
        Changed(nameof(TaskPermissionHint));
        Changed(nameof(LoadWarning));
        Changed(nameof(HasWarning));
        Changed(nameof(IsUnavailable));
        Changed(nameof(ShowRestore));
        Changed(nameof(BackupLabel));
    }

    private void SetBusy(bool value)
    {
        busy = value;
        Changed(nameof(CanEdit));
        Changed(nameof(CanEditTasks));
        Changed(nameof(CanRestore));
    }

    private void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
