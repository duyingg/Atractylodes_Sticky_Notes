using System.ComponentModel;
using System.Diagnostics;

namespace Memo.Desktop.Windows;

internal readonly record struct PriorityStatus(string Current, string? Error);

internal static class ProcessScheduling
{
    public static PriorityStatus Read()
    {
        using var process = Process.GetCurrentProcess();
        try
        {
            return new(Label(process.PriorityClass), null);
        }
        catch (Win32Exception error)
        {
            return new("无法读取", $"Windows 无法读取进程优先级（错误 {error.NativeErrorCode}）。");
        }
    }

    public static PriorityStatus Apply(Models.PriorityPreset preset)
    {
        var desired = preset switch
        {
            Models.PriorityPreset.Low => ProcessPriorityClass.Idle,
            Models.PriorityPreset.Normal => ProcessPriorityClass.Normal,
            Models.PriorityPreset.AboveNormal => ProcessPriorityClass.AboveNormal,
            Models.PriorityPreset.High => ProcessPriorityClass.High,
            _ => ProcessPriorityClass.BelowNormal
        };

        using var process = Process.GetCurrentProcess();
        try
        {
            process.PriorityClass = desired;
            process.Refresh();
            var current = process.PriorityClass;
            return new(Label(current), current == desired ? null : "Windows 未采用所选优先级。");
        }
        catch (Win32Exception error)
        {
            var message = error.NativeErrorCode == 5
                ? "Windows 拒绝访问（错误 5）。当前进程权限不足，优先级未应用。"
                : $"Windows 未能应用所选优先级（错误 {error.NativeErrorCode}）。";
            return new(Read().Current, message);
        }
    }

    private static string Label(ProcessPriorityClass value) => value switch
    {
        ProcessPriorityClass.Idle => "低",
        ProcessPriorityClass.BelowNormal => "低于正常",
        ProcessPriorityClass.Normal => "正常",
        ProcessPriorityClass.AboveNormal => "高于正常",
        ProcessPriorityClass.High => "高",
        ProcessPriorityClass.RealTime => "实时",
        _ => value.ToString()
    };
}
