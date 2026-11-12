using Memo.Desktop.Models;
using Memo.Desktop.Services;
using Memo.Desktop.ViewModels;
using Memo.Desktop.Windows;
using static Memo.Desktop.Windows.BoardGeometry.ResizeEdges;

var directory = Directory.CreateTempSubdirectory("memo-smoke-").FullName;
var store = new JsonMemoStore(directory);

try
{
    var empty = store.Load();
    Require(empty.Warning is null && empty.Document.Tasks.Length == 0 && empty.Backup == BackupStatus.Missing, "first launch");
    var model = new MemoViewModel(store, empty);

    Require(await model.SaveTaskAsync(null, "  ") is not null && !File.Exists(store.Path), "reject blank");
    Require(await model.SaveTaskAsync(null, "  写便签  ") is null, "add Chinese task");
    Require(await model.SaveTaskAsync(null, "第二项") is null, "append task");
    Require(model.Tasks.Select(task => task.Text).SequenceEqual(["写便签", "第二项"]), "trim and maintain order");

    var first = model.Tasks[0];
    var reopened = new MemoViewModel(store, store.Load());
    Require(await reopened.SaveTaskAsync(first, "修改后的任务") is null, "edit after reload");
    var second = reopened.Tasks[1];
    Require(await reopened.ToggleAsync(first) is null && reopened.Tasks[^1].Id == first.Id && reopened.Tasks[^1].IsCompleted, "complete task");
    Require(await reopened.DeleteAsync(second) is null, "delete task");
    var layout = new BoardLayout(360, -500, 50, 410);
    Require(await reopened.SetLayoutAsync(layout) is null, "save layout");

    var savedState = store.Load();
    var saved = savedState.Document;
    Require(savedState.Warning is null && savedState.Backup == BackupStatus.Available && saved.Layout == layout && saved.Tasks.Length == 1
        && saved.Tasks[0] == new MemoTask(first.Id, "修改后的任务", true), "round trip after edits");
    Console.WriteLine("PASS add / edit / complete / delete / layout / reload");

    var monitor = new BoardGeometry.Rect { Left = -1000, Top = 0, Right = 0, Bottom = 800 };
    var work = new BoardGeometry.Rect { Left = -1000, Top = 0, Right = 0, Bottom = 760 };
    var board = new BoardGeometry.Rect { Left = -800, Top = 90, Right = -300, Bottom = 540 };
    var placement = new BoardGeometry.Placement(monitor, work, board, 120, false);
    var right = BoardGeometry.Resize(placement, Right, new(180, 50));
    Require(right.Left == board.Left && right.Width == 680 && right.Height == board.Height,
        "right edge adjusts only width");
    var top = BoardGeometry.Resize(placement, Top, new(50, 900));
    Require(top.Bottom == board.Bottom && top.Height == 300 && top.Width == board.Width,
        "top edge preserves bottom anchor and respects the scaled minimum");
    var corner = BoardGeometry.Resize(placement, Left | Bottom, new(-500, 500));
    Require(corner.Left == work.Left && corner.Bottom == work.Bottom
        && corner.Top == board.Top && corner.Right == board.Right, "corner stays within the work area");
    var capped = BoardGeometry.Resize(placement, Right, new(900, 0));
    Require(capped.Width == 800, "right edge respects the scaled maximum");
    Console.WriteLine("PASS rectangular border resize, opposite anchors, scaled limits and work area");

    var notePosition = BoardGeometry.AdjacentNoteLayout(placement);
    Require(notePosition.X > board.Right && notePosition.X + notePosition.Edge * placement.Scale <= work.Right,
        "both windows fit side by side on the work area");

    var held = Path.Combine(directory, "state.held");
    File.Move(store.Path, held);
    Directory.CreateDirectory(store.Path);
    try
    {
        Require(await reopened.SaveTaskAsync(null, "写入失败") is not null, "report failed write");
        Require(reopened.Tasks.Length == 1 && reopened.CanEdit, "keep prior model and permit retry");
    }
    finally
    {
        Directory.Delete(store.Path);
        File.Move(held, store.Path);
    }
    Require(await reopened.SaveTaskAsync(null, "重试成功") is null, "retry");
    Require(!Directory.GetFiles(directory, "*.tmp").Any(), "no stray temporary files");
    Console.WriteLine("PASS interrupted write keeps prior state and retries");

    File.WriteAllText(store.Path, "{ broken");
    var priorBackup = File.ReadAllBytes(store.Path + ".bak");
    Require(await reopened.SaveTaskAsync(null, "外部损坏后尝试保存") is not null
        && File.ReadAllText(store.Path) == "{ broken"
        && File.ReadAllBytes(store.Path + ".bak").SequenceEqual(priorBackup), "external corruption cannot overwrite a valid backup");
    var backup = store.Load();
    Require(backup.Warning is not null && backup.Document.Tasks.SequenceEqual(saved.Tasks), "read last backup");
    var recovered = new MemoViewModel(store, backup);
    Require(recovered.CanRestore && !recovered.CanEdit, "valid backup is viewable and restorable");
    Require(await recovered.DeleteAsync(backup.Document.Tasks[0]) is not null
        && File.ReadAllText(store.Path) == "{ broken", "preserve corrupted primary");

    using (var locked = File.Open(store.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        var failedRestore = await recovered.RestoreAsync();
        Require(failedRestore.Error is not null && recovered.CanRestore && !recovered.CanEdit, "failed restore remains read-only");
    }
    var backupBefore = File.ReadAllBytes(store.Path + ".bak");
    var (restoreError, archive) = await recovered.RestoreAsync();
    Require(restoreError is null && archive is not null && recovered.CanEdit && !recovered.CanRestore, "restore resumes editing");
    Require(File.ReadAllText(archive!) == "{ broken" && File.ReadAllBytes(store.Path + ".bak").SequenceEqual(backupBefore), "archive original and keep the backup");
    var afterRestore = store.Load();
    Require(afterRestore.Warning is null && afterRestore.Document.Tasks.SequenceEqual(saved.Tasks) && afterRestore.Document.Layout == saved.Layout, "restore tasks and layout");

    var future = """{"schemaVersion":2,"tasks":[],"layout":{"edge":320}}""";
    File.WriteAllText(store.Path + ".bak", "invalid backup");
    File.WriteAllText(store.Path, future);
    Require(store.Load().Warning is not null && File.ReadAllText(store.Path) == future,
        "preserve unknown schema");
    var unavailable = new MemoViewModel(store, store.Load());
    Require(unavailable.IsUnavailable && !unavailable.CanRestore && !unavailable.CanEdit, "both files unreadable");
    Console.WriteLine("PASS read-only fallback, failed restore, lossless recovery, unavailable state and schema protection");

    File.Delete(store.Path);
    File.Delete(store.Path + ".bak");
    var a = new MemoTask(Guid.NewGuid(), "待办甲");
    var b = new MemoTask(Guid.NewGuid(), "待办乙");
    var done1 = new MemoTask(Guid.NewGuid(), "完成甲", true);
    var done2 = new MemoTask(Guid.NewGuid(), "完成乙", true);
    store.Save(new MemoDocument(1, [done1, a, done2, b], new BoardLayout()));
    var ordered = new MemoViewModel(store, store.Load());
    Require(ordered.Tasks.SequenceEqual([a, b, done1, done2]), "group legacy interleaved tasks without losing within-group order");
    Require(await ordered.SaveTaskAsync(null, "新建任务") is null, "create a pending task");
    var created = ordered.Tasks[2];
    Require(created.Text == "新建任务" && ordered.Tasks.SequenceEqual([a, b, created, done1, done2]),
        "new task is last in pending group");
    Require(await ordered.MoveAsync(created.Id, a.Id, after: false) is null
        && ordered.Tasks.SequenceEqual([created, a, b, done1, done2]), "reorder pending task");
    Require(await ordered.MoveAsync(done2.Id, a.Id, after: true) is null
        && ordered.Tasks.SequenceEqual([created, a, b, done2, done1]), "drop across groups clamps to the group boundary");
    Require(await ordered.ToggleAsync(a) is null && ordered.Tasks[^1] == a with { IsCompleted = true }, "completed task goes below pending tasks");
    Require(await ordered.ToggleAsync(done2) is null && ordered.Tasks[2] == done2 with { IsCompleted = false }, "reopened task goes to pending group end");
    Require(store.Load().Document.Tasks.SequenceEqual(ordered.Tasks), "saved order round trips");
    Console.WriteLine("PASS legacy grouping, append, drag order, completion and reopen");

    Require(ShortcutChord.TryParse(" alt + ctrl + f3 ", out var chord)
        && chord.Text == "Ctrl+Alt+F3" && chord.Modifiers == 3 && chord.Key == 0x72, "normalize shortcut");
    Require(!ShortcutChord.TryParse("Win+N", out _) && !ShortcutChord.TryParse("Ctrl+Ctrl+N", out _)
        && !ShortcutChord.TryParse("Ctrl+C", out _) && !ShortcutChord.TryParse("Ctrl+Alt+F12", out _), "reject unsupported shortcut");
    Require(ordered.Behavior.Priority == PriorityPreset.BelowNormal, "older state defaults to below normal");
    var behavior = new BehaviorOptions(true, false, ShortcutKeys.Default with { Wake = "Ctrl+Shift+M" }, PriorityPreset.Normal);
    Require(await ordered.SetBehaviorAsync(behavior) is null && store.Load().Document.Behavior == behavior, "shortcut and pin settings survive reload");
    Console.WriteLine("PASS shortcut parsing and persisted behavior");

    Require(await ordered.SetTaskLockAsync(true) is null && !ordered.CanEditTasks && ordered.CanEdit, "lock tasks but keep settings editable");
    var lockedModel = new MemoViewModel(store, store.Load());
    var before = lockedModel.Tasks.ToArray();
    Require(lockedModel.TasksLocked && !lockedModel.CanEditTasks, "task lock persists");
    Require(await lockedModel.SaveTaskAsync(null, "不应新增") is not null
        && await lockedModel.SaveTaskAsync(before[0], "不应修改") is not null
        && await lockedModel.ToggleAsync(before[0]) is not null
        && await lockedModel.DeleteAsync(before[0]) is not null
        && await lockedModel.MoveAsync(before[0].Id, before[^1].Id, true) is not null
        && lockedModel.Tasks.SequenceEqual(before), "locked tasks reject all mutation entry points");
    Require(await lockedModel.SetLayoutAsync(new BoardLayout(375, 200, 100)) is null
        && store.Load().Document.Layout is { Edge: 375, Height: null, EffectiveHeight: 375 },
        "legacy square size persists while task edits are locked");
    Require(lockedModel.Display == DisplayOptions.Default && lockedModel.Note == "", "old snapshots show the task panel only");
    Require(await lockedModel.SetDisplayAsync(new DisplayOptions(FontSize: 25)) is not null, "invalid font size rejected");
    var display = new DisplayOptions(ShowNote: true, AddAtTop: true, FontSize: 21, Skin: SkinPreset.Night, NoteLines: true);
    const string note = "  第一行\r\n第二行\t ";
    Require(await lockedModel.SetDisplayAsync(display) is null && await lockedModel.SetNoteAsync(note) is null,
        "display and notes can be saved when task editing is locked");
    var savedNote = new MemoViewModel(store, store.Load());
    Require(savedNote.Display == display && savedNote.Note == note && savedNote.TasksLocked
        && savedNote.TaskPreviewHeight == 55, "both panels, font and plain-text note survive reload");
    var noPanels = display with { ShowTasks = false, ShowNote = false };
    Require(await savedNote.SetDisplayAsync(display with { ShowTasks = false }) is null
        && await savedNote.SetDisplayAsync(noPanels) is not null, "only one panel can be hidden while both are open");
    var closed = new MemoViewModel(store, store.Load());
    Require(closed.Note == note && closed.Display is { ShowTasks: false, ShowNote: true, Skin: SkinPreset.Night, NoteLines: true },
        "the last visible panel, note, skin and rules survive reload");
    var legacyNoPanels = store.Load().Document with { Display = noPanels };
    store.Save(legacyNoPanels);
    Require(store.Load().Document.Display == noPanels with { ShowTasks = true }
        && store.Load().Document.Note == note, "recover a visible panel from an older all-hidden snapshot");
    Console.WriteLine("PASS panel selection, font size and independent note persistence");
    Require(await lockedModel.SetTaskLockAsync(false) is null && lockedModel.CanEditTasks
        && await lockedModel.SaveTaskAsync(null, "解锁后新增") is null, "unlock restores editing");
    Console.WriteLine("PASS durable task lock and independent layout settings");

    store.Save(MemoDocument.Empty);
    var weekly = new MemoViewModel(store, store.Load());
    var monday = new DateOnly(2026, 11, 9);
    var tuesday = monday.AddDays(1);
    Require(await weekly.SaveTaskAsync(null, "手动待办") is null, "create a manual task before configuring defaults");
    var manual = weekly.Tasks.Single();
    Require(await weekly.SaveWeeklyTaskAsync(null, DayOfWeek.Monday, "周一晨间", monday) is null
        && await weekly.SaveWeeklyTaskAsync(null, DayOfWeek.Monday, "周一午间", monday) is null
        && await weekly.SaveWeeklyTaskAsync(null, DayOfWeek.Tuesday, "周二晨间", monday) is null, "save weekday templates");
    var morning = weekly.Tasks[0];
    var noon = weekly.Tasks[1];
    Require(weekly.Tasks.Select(task => task.Text).SequenceEqual(["周一晨间", "周一午间", "手动待办"])
        && morning.ScheduleId is not null && noon.ScheduleId is not null, "only today's defaults go above manual tasks");
    Require(await weekly.ToggleAsync(morning) is null && weekly.Tasks[^1] == manual
        && weekly.Tasks[1].Id == morning.Id && weekly.Tasks[1].IsCompleted, "completed defaults stay above manual tasks");
    Require(await weekly.MoveAsync(manual.Id, noon.Id, after: false) is null && weekly.Tasks[^1] == manual,
        "drag cannot put a manual task above the daily group");
    Require(await weekly.DeleteAsync(noon) is null, "delete today's instance in task management");
    var reopenedWeek = new MemoViewModel(store, store.Load());
    Require(await reopenedWeek.RefreshDailyTasksAsync(monday) is null && reopenedWeek.Tasks.Length == 2
        && reopenedWeek.Tasks[0].Id == morning.Id && reopenedWeek.Tasks[0].IsCompleted,
        "same-day restart neither duplicates defaults nor restores a deleted instance");
    Require(await reopenedWeek.SaveWeeklyTaskAsync(morning.ScheduleId, DayOfWeek.Monday, "周一早会", monday) is null
        && reopenedWeek.Tasks[0].Id == morning.Id && reopenedWeek.Tasks[0].Text == "周一早会"
        && reopenedWeek.Tasks[0].IsCompleted && reopenedWeek.Tasks.Length == 2,
        "today's template edits retain completion and don't restore a different deleted instance");
    Require(await reopenedWeek.RefreshDailyTasksAsync(tuesday) is null
        && reopenedWeek.Tasks.Select(task => task.Text).SequenceEqual(["周二晨间", "手动待办"]),
        "new day replaces only scheduled tasks");
    Require(await reopenedWeek.RefreshDailyTasksAsync(monday.AddDays(7)) is null
        && reopenedWeek.Tasks.Select(task => task.Text).SequenceEqual(["周一早会", "周一午间", "手动待办"])
        && reopenedWeek.Tasks[0].Id != morning.Id && !reopenedWeek.Tasks[0].IsCompleted,
        "next week creates fresh instances");
    Require(await reopenedWeek.DeleteWeeklyTaskAsync(noon.ScheduleId!.Value, monday.AddDays(7)) is null
        && reopenedWeek.Tasks.All(task => task.ScheduleId != noon.ScheduleId)
        && store.Load().Document.Tasks.Last() == manual,
        "removing a template removes its current instance and preserves manual tasks");
    Console.WriteLine("PASS weekly defaults, rollover, same-day deletion, grouping and persistence");
}
finally
{
    foreach (var file in Directory.EnumerateFiles(directory))
        File.Delete(file);
    Directory.Delete(directory);
}

static void Require(bool valid, string scenario)
{
    if (!valid)
        throw new InvalidOperationException(scenario);
}
