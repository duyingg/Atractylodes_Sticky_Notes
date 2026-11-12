using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Memo.Desktop.Models;
using Memo.Desktop.ViewModels;

namespace Memo.Desktop.Views;

public partial class SkinSettings : UserControl
{
    private MemoViewModel? model;
    private SkinPreset? shownSkin;

    public SkinSettings()
    {
        InitializeComponent();
        DataContextChanged += BindModel;
    }

    private void BindModel(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (model is not null)
            model.PropertyChanged -= OnModelChanged;
        model = e.NewValue as MemoViewModel;
        if (model is null)
            return;
        model.PropertyChanged += OnModelChanged;
        shownSkin = null;
        RefreshSelection();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MemoViewModel.Display))
            RefreshSelection();
    }

    private void RefreshSelection()
    {
        var skin = model!.Display.Skin;
        if (shownSkin == skin)
            return;
        shownSkin = skin;
        Night.IsChecked = skin == SkinPreset.Night;
        Day.IsChecked = skin != SkinPreset.Night;
    }

    private async void Apply(object sender, RoutedEventArgs e)
    {
        if (model is null)
            return;
        var skin = Night.IsChecked == true ? SkinPreset.Night : SkinPreset.Day;
        var error = await model.SetDisplayAsync(model.Display with { Skin = skin });
        Status.SetResourceReference(TextBlock.ForegroundProperty, error is null ? "Accent" : "Danger");
        Status.Text = error ?? "皮肤已保存。";
    }
}
