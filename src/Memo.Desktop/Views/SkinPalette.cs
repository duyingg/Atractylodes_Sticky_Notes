using System.Windows;
using System.Windows.Media;
using Memo.Desktop.Models;

namespace Memo.Desktop.Views;

internal sealed class SkinPalette
{
    private static readonly Dictionary<string, string> NightColors = new()
    {
        ["Ink"] = "#E4EDE8",
        ["Second"] = "#B5C6BC",
        ["Muted"] = "#9AAEA2",
        ["Accent"] = "#8BDAB9",
        ["Wash"] = "#30473E",
        ["Edge"] = "#41544A",
        ["Paper"] = "#202923",
        ["Light"] = "#27332C",
        ["Sidebar"] = "#222E27",
        ["Danger"] = "#FF9C95",
        ["DangerWash"] = "#452F2D",
        ["Surface"] = "#1B241E",
        ["OnAccent"] = "#173328",
        ["ButtonHover"] = "#24B4E2CE",
        ["ButtonPressed"] = "#42B4E2CE",
        ["HoverWash"] = "#34443A",
        ["ScrollThumb"] = "#718F80",
        ["CheckEdge"] = "#799488",
        ["PreviewBackground"] = "#253337",
        ["PreviewBorder"] = "#51676C",
        ["MonitorFill"] = "#455C62",
        ["WorkFill"] = "#354C51",
        ["NoteRule"] = "#43584D"
    };

    private readonly ResourceDictionary resources;
    private readonly Dictionary<string, object> dayBrushes;
    private SkinPreset applied = SkinPreset.Day;

    internal SkinPalette(ResourceDictionary resources)
    {
        this.resources = resources;
        dayBrushes = NightColors.Keys.ToDictionary(key => key, key => resources[key]);
    }

    internal void Apply(SkinPreset skin)
    {
        if (applied == skin)
            return;
        foreach (var (key, color) in NightColors)
            resources[key] = skin == SkinPreset.Night ? NightBrush(color) : dayBrushes[key];
        applied = skin;
    }

    private static SolidColorBrush NightBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
