using System.Windows;
using System.Windows.Controls;
using Memo.Desktop.ViewModels;

namespace Memo.Desktop.Views;

public partial class PanelSettings : UserControl
{
    public PanelSettings() => InitializeComponent();

    private async void Apply(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MemoViewModel model)
            return;
        var error = await model.SetDisplayAsync(model.Display with
        {
            ShowTasks = ShowTasks.IsChecked == true,
            ShowNote = ShowNote.IsChecked == true,
            NoteLines = NoteLines.IsChecked == true
        });
        Status.SetResourceReference(TextBlock.ForegroundProperty, error is null ? "Accent" : "Danger");
        Status.Text = error ?? "已保存。";
    }
}
