using System.IO;
using System.Text.Json;
using Memo.Desktop.Models;

namespace Memo.Desktop.Services;

internal enum BackupStatus { Missing, Available, Unreadable }

internal sealed record MemoLoadResult(MemoDocument Document, string? Warning, BackupStatus Backup);

internal sealed class JsonMemoStore(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string DirectoryPath => directory;
    public string Path { get; } = System.IO.Path.Combine(directory, "state.json");
    private string BackupPath => Path + ".bak";

    public MemoLoadResult Load()
    {
        var primary = TryRead(Path);
        var backup = TryRead(BackupPath);
        if (primary.Document is { } saved)
            return new(saved, null, backup.Status);
        if (primary.Status == BackupStatus.Missing && backup.Status == BackupStatus.Missing)
            return new(MemoDocument.Empty, null, BackupStatus.Missing);

        var warning = backup.Status == BackupStatus.Available
            ? "正在以只读方式显示上次备份。原始数据文件已保留。"
            : "数据文件与备份暂不可读，已暂停编辑并保留现有文件。";
        return new(backup.Document ?? MemoDocument.Empty, warning, backup.Status);
    }

    private static (MemoDocument? Document, BackupStatus Status) TryRead(string path)
    {
        try
        {
            return (Read(path), BackupStatus.Available);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return (null, BackupStatus.Missing);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return (null, BackupStatus.Unreadable);
        }
    }

    private static MemoDocument Read(string path)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<MemoDocument>(stream, JsonOptions)
            ?? throw new JsonException("空便签文件");
        if (document.SchemaVersion != MemoDocument.CurrentSchema)
            throw new NotSupportedException("未知便签文件版本");
        if (document.Tasks is null || document.Layout is null
            || document.Layout.Edge is < BoardLayout.MinimumEdge or > BoardLayout.MaximumEdge
            || document.Layout.Height is < BoardLayout.MinimumEdge or > BoardLayout.MaximumEdge
            || document.Display?.FontSize is < DisplayOptions.MinimumFontSize or > DisplayOptions.MaximumFontSize
            || (document.Layout.X is null) != (document.Layout.Y is null)
            || document.Tasks.Any(task => task is null || task.Id == Guid.Empty
                || task.ScheduleId == Guid.Empty || !ValidTaskText(task.Text))
            || document.Tasks.Select(task => task.Id).Distinct().Count() != document.Tasks.Length
            || !ValidSchedule(document))
        {
            throw new JsonException("便签数据无效");
        }
        return document.Display is { HasVisiblePanel: false } display
            ? document with { Display = display with { ShowTasks = true } }
            : document;
    }

    private static bool ValidTaskText(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= MemoDocument.MaximumTaskLength;

    private static bool ValidSchedule(MemoDocument document)
    {
        var schedule = document.WeeklySchedule ?? [];
        if (schedule.Any(item => item is null || item.Id == Guid.Empty || !Enum.IsDefined(item.Day) || !ValidTaskText(item.Text))
            || schedule.Select(item => item.Id).Distinct().Count() != schedule.Length)
            return false;

        var scheduled = document.Tasks.Where(task => task.ScheduleId is not null).Select(task => task.ScheduleId!.Value).ToArray();
        if (scheduled.Length == 0)
            return true;
        if (document.ScheduleDate is not { } date || scheduled.Distinct().Count() != scheduled.Length)
            return false;
        var defaults = schedule.Where(item => item.Day == date.DayOfWeek).Select(item => item.Id).ToHashSet();
        return scheduled.All(defaults.Contains);
    }

    public BackupStatus Save(MemoDocument document)
    {
        if (TryRead(Path).Status == BackupStatus.Unreadable)
            throw new IOException("现有数据文件不可读，已保留原文件和备份。");

        return Write(document, BackupPath) ? BackupStatus.Available : TryRead(BackupPath).Status;
    }

    public (MemoDocument Document, string? ArchivedFile) Restore()
    {
        var document = Read(BackupPath);
        var archive = Path + $".original-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        return (document, Write(document, archive) ? archive : null);
    }

    // File.Replace keeps the previous file at the requested backup path as it installs the new snapshot.
    private bool Write(MemoDocument document, string replacementBackup)
    {
        Directory.CreateDirectory(directory);
        var temporary = System.IO.Path.Combine(directory, $"state.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, document, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(Path))
            {
                File.Replace(temporary, Path, replacementBackup);
                return true;
            }

            File.Move(temporary, Path);
            return false;
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A temporary file is never loaded as a committed snapshot.
            }
        }
    }
}
