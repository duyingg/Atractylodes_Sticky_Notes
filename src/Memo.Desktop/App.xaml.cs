using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Memo.Desktop.Models;
using Memo.Desktop.Services;
using Memo.Desktop.ViewModels;
using Memo.Desktop.Views;
using Memo.Desktop.Windows;

namespace Memo.Desktop;

public partial class App : Application
{
    private SingleInstance? instance;
    private MemoTray? tray;
    private GlobalShortcuts? shortcuts;
    private MemoViewModel? model;
    private SettingsWindow? settings;
    private BoardWindow? board;
    private NoteWindow? note;
    private SkinPalette? skinPalette;
    private DispatcherTimer? dailyTimer;
    private string? dailyError;
    private bool exiting;
    private PriorityPreset? configuredPriority;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Memo");
        var profile = "";
#if DEBUG
        if (Environment.GetEnvironmentVariable("MEMO_TEST_DIRECTORY") is { Length: > 0 } testDirectory)
        {
            directory = Path.GetFullPath(testDirectory);
            profile = "." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory)));
        }
#endif
        instance = new SingleInstance(profile);
        if (!instance.IsPrimary)
        {
            await instance.SignalAsync();
            Shutdown();
            return;
        }

        var store = new JsonMemoStore(directory);
        var initial = await Task.Run(store.Load);
        model = new MemoViewModel(store, initial);
        dailyError = await model.RefreshDailyTasksAsync(DateOnly.FromDateTime(DateTime.Now));
        skinPalette = new SkinPalette(Resources);
        skinPalette.Apply(model.Display.Skin);
        settings = new SettingsWindow(model);
        board = new BoardWindow(model);
        if (dailyError is not null)
            board.ShowError(dailyError);
        note = new NoteWindow(model);
        board.ContextMenu = PanelContextMenu.Create(() => ClosePanel(isNote: false), RequestExit);
        note.SetPanelMenu(() => ClosePanel(isNote: true), RequestExit);
        settings.ExitRequested += (_, _) => RequestExit();
        board.DataRequested += () =>
        {
            if (!exiting)
                settings.OpenData();
        };
        board.PlacementChanged += placement =>
        {
            settings.UpdatePlacement(placement);
            if (model.Display.ShowTasks && model.Display.ShowNote)
                note.PlaceBeside(placement);
        };
        board.PositionSaved += settings.PositionSaved;
        settings.WakeRequested += WakePanels;
        settings.ShortcutControls.ShortcutsSaved += ConfigureShortcuts;
        settings.BehaviorControls.PriorityRetryRequested += () => ConfigurePriority(force: true);
        model.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(MemoViewModel.Display))
            {
                skinPalette.Apply(model.Display.Skin);
                UpdatePanels();
            }
            if (changed.PropertyName != nameof(MemoViewModel.Behavior))
                return;
            ConfigureShortcuts();
            ConfigurePriority();
        };
        MainWindow = settings;
        ConfigurePriority();

        tray = new MemoTray(Dispatcher, OpenSettings, RequestExit);
        UpdatePanels();
        shortcuts = new GlobalShortcuts();
        shortcuts.Invoked += HandleShortcut;
        ConfigureShortcuts();
        dailyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        dailyTimer.Tick += RefreshDailyTasks;
        dailyTimer.Start();
        _ = instance.ListenAsync(OpenSettings);
    }

    private async void RefreshDailyTasks(object? sender, EventArgs e)
    {
        if (exiting || model is not { CanEdit: true })
            return;
        var error = await model.RefreshDailyTasksAsync(DateOnly.FromDateTime(DateTime.Now));
        if (dailyError != error)
            board?.ShowError(error ?? "");
        dailyError = error;
    }

    private void ConfigurePriority(bool force = false)
    {
        if (model is null || settings is null)
            return;
        var priority = model.Behavior.Priority;
        if (!force && configuredPriority == priority)
            return;

        configuredPriority = priority;
        settings.BehaviorControls.ShowPriority(ProcessScheduling.Apply(priority), applied: true);
    }

    private void ConfigureShortcuts()
    {
        if (model is not null && shortcuts is not null && settings is not null)
            settings.ShortcutControls.UpdateShortcuts(shortcuts.Configure(model.Behavior));
    }

    private void UpdatePanels()
    {
        if (exiting || model is null || board is null || note is null)
            return;
        if (model.Display.ShowTasks && !board.IsVisible)
            board.Show();
        else if (!model.Display.ShowTasks && board.IsVisible)
            board.Hide();

        if (model.Display.ShowNote)
        {
            note.PlaceBeside(board.IsVisible ? board.Placement : null);
            if (!note.IsVisible)
                note.Show();
        }
        else if (note.IsVisible)
            note.Hide();
    }

    private void WakePanels()
    {
        if (board is { IsVisible: true })
            board.Wake();
        if (note is { IsVisible: true })
            note.Wake();
    }

    private async void ClosePanel(bool isNote)
    {
        if (exiting || model is null)
            return;
        if (isNote && note is not null && await note.FlushAsync() is { } noteError)
        {
            MessageBox.Show(noteError, "便签栏", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        await model.WaitForSaveAsync();
        if (exiting)
            return;
        var options = model.Display;
        var next = isNote ? options with { ShowNote = false } : options with { ShowTasks = false };
        if (!next.HasVisiblePanel)
        {
            RequestExit();
            return;
        }
        var error = await model.SetDisplayAsync(next);
        if (error is not null)
            MessageBox.Show(error, isNote ? "便签栏" : "待办栏", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void QuickAdd()
    {
        if (board is { IsVisible: true })
            board.QuickAdd();
        else if (model is { TaskReadOnly: true })
            settings?.OpenData();
        else
            settings?.QuickAddTask();
    }

    private async void HandleShortcut(ShortcutAction action)
    {
        if (exiting || model is null || board is null)
            return;

        switch (action)
        {
            case ShortcutAction.Wake:
                WakePanels();
                break;
            case ShortcutAction.Pin:
                var error = await model.SetBehaviorAsync(model.Behavior with { AlwaysOnTop = !model.Behavior.AlwaysOnTop });
                if (error is not null)
                {
                    if (board.IsVisible)
                        board.ShowError(error);
                    else
                        MessageBox.Show(error, "行为设置", MessageBoxButton.OK, MessageBoxImage.Error);
                    WakePanels();
                }
                break;
            case ShortcutAction.Add:
                QuickAdd();
                break;
            case ShortcutAction.Settings:
                OpenSettings();
                break;
        }
    }

    private void OpenSettings()
    {
        if (!exiting)
            settings?.Open();
    }

    private async void RequestExit()
    {
        if (exiting || model is null)
            return;

        exiting = true;
        if (settings is not null)
            settings.IsEnabled = false;
        if (board is not null)
            board.IsEnabled = false;
        if (note is not null)
            note.IsEnabled = false;

        await model.WaitForSaveAsync();
        if (note is not null && await note.FlushAsync() is { } error)
        {
            exiting = false;
            if (settings is not null)
                settings.IsEnabled = true;
            if (board is not null)
                board.IsEnabled = true;
            note.IsEnabled = true;
            MessageBox.Show(error, "便签栏", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        settings?.PermitClose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        dailyTimer?.Stop();
        shortcuts?.Dispose();
        tray?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}
