using System.Text.Json.Serialization;

namespace Memo.Desktop.Models;

internal sealed record MemoTask(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired] string Text,
    [property: JsonRequired] bool IsCompleted = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ScheduleId = null)
{
    [JsonIgnore]
    public int SortGroup => (ScheduleId is null ? 2 : 0) + (IsCompleted ? 1 : 0);
}

internal sealed record WeeklyTask(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired] DayOfWeek Day,
    [property: JsonRequired] string Text);

// The persisted Edge field is the width. Older snapshots used it for both dimensions.
internal sealed record BoardLayout(double Edge = 320, int? X = null, int? Y = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Height = null)
{
    public const int MinimumEdge = 240;
    public const int MaximumEdge = 640;

    [JsonIgnore]
    public double EffectiveHeight => Height ?? Edge;
}

internal sealed record MemoDocument(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] MemoTask[] Tasks,
    [property: JsonRequired] BoardLayout Layout,
    BehaviorOptions? Behavior = null, bool TasksLocked = false,
    DisplayOptions? Display = null, string? Note = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WeeklyTask[]? WeeklySchedule = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? ScheduleDate = null)
{
    public const int CurrentSchema = 1;
    public const int MaximumTaskLength = 240;
    public static MemoDocument Empty => new(CurrentSchema, [], new BoardLayout());

    public MemoDocument WithGroupedTasks() => this with { Tasks = Tasks.OrderBy(task => task.SortGroup).ToArray() };

    public MemoDocument WithDailyTasks(DateOnly date, WeeklyTask[]? previousSchedule = null)
    {
        if (ScheduleDate == date && previousSchedule is null)
            return this;

        var defaults = (WeeklySchedule ?? []).Where(item => item.Day == date.DayOfWeek).ToArray();
        if (ScheduleDate != date)
        {
            return (this with
            {
                ScheduleDate = date,
                Tasks = [.. Tasks.Where(task => task.ScheduleId is null), .. defaults.Select(CreateDailyTask)]
            }).WithGroupedTasks();
        }

        var before = (previousSchedule ?? []).Where(item => item.Day == date.DayOfWeek).ToDictionary(item => item.Id);
        var after = defaults.ToDictionary(item => item.Id);
        // Reconcile template changes without bringing back tasks deleted for this date.
        var existing = Tasks.Where(task => task.ScheduleId is not { } id || after.ContainsKey(id)).Select(task =>
            task.ScheduleId is { } id && before.TryGetValue(id, out var previous) && previous.Text != after[id].Text
                ? task with { Text = after[id].Text } : task);
        var added = defaults.Where(item => !before.ContainsKey(item.Id)).Select(CreateDailyTask);
        return (this with { Tasks = [.. existing, .. added] }).WithGroupedTasks();
    }

    private static MemoTask CreateDailyTask(WeeklyTask item) =>
        new(Guid.NewGuid(), item.Text, ScheduleId: item.Id);
}

internal enum SkinPreset { Day, Night }

internal sealed record DisplayOptions(bool ShowTasks = true, bool ShowNote = false, bool AddAtTop = false, int FontSize = 13,
    SkinPreset Skin = SkinPreset.Day, bool NoteLines = false)
{
    public const int MinimumFontSize = 12;
    public const int MaximumFontSize = 24;
    public static DisplayOptions Default { get; } = new();

    [JsonIgnore]
    public bool HasVisiblePanel => ShowTasks || ShowNote;
}

internal enum ShortcutAction { Wake, Pin, Add, Settings }

internal sealed record ShortcutKeys(string Wake = "Ctrl+Alt+M", string Pin = "Ctrl+Alt+T",
    string Add = "Ctrl+Alt+N", string Settings = "Ctrl+Alt+O")
{
    public static ShortcutKeys Default { get; } = new();

    public string Get(ShortcutAction action) => action switch
    {
        ShortcutAction.Wake => Wake,
        ShortcutAction.Pin => Pin,
        ShortcutAction.Add => Add,
        ShortcutAction.Settings => Settings,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}

internal enum PriorityPreset { BelowNormal, Low, Normal, AboveNormal, High }

internal sealed record BehaviorOptions(bool AlwaysOnTop = false, bool ShortcutsEnabled = true, ShortcutKeys? Keys = null,
    PriorityPreset Priority = PriorityPreset.BelowNormal)
{
    public static BehaviorOptions Default { get; } = new();
    [JsonIgnore]
    public ShortcutKeys EffectiveKeys => Keys ?? ShortcutKeys.Default;
}

internal readonly record struct ShortcutChord(uint Modifiers, uint Key, string Text)
{
    public static bool TryParse(string? text, out ShortcutChord chord)
    {
        chord = default;
        var parts = text?.Split('+', StringSplitOptions.TrimEntries);
        if (parts is null || parts.Length is < 3 or > 4)
            return false;

        uint modifiers = 0;
        foreach (var part in parts[..^1])
        {
            var bit = part.ToUpperInvariant() switch { "ALT" => 1u, "CTRL" => 2u, "SHIFT" => 4u, _ => 0u };
            if (bit == 0 || (modifiers & bit) != 0)
                return false;
            modifiers |= bit;
        }
        if ((modifiers & 3) == 0)
            return false;

        var key = parts[^1].ToUpperInvariant();
        uint vk;
        if (key.Length == 1 && key[0] is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            vk = key[0];
        else if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out var function) && function is >= 1 and <= 11
            && key == $"F{function}")
            vk = (uint)(0x6f + function);
        else
            return false;

        var normalized = ((modifiers & 2) != 0 ? "Ctrl+" : "")
            + ((modifiers & 1) != 0 ? "Alt+" : "")
            + ((modifiers & 4) != 0 ? "Shift+" : "") + key;
        chord = new ShortcutChord(modifiers, vk, normalized);
        return true;
    }
}
