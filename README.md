# 白术便签

白术便签是 Windows 桌面待办和纯文本便签工具。设置从系统托盘打开，可以单独显示待办栏或便签栏，也可以同时使用。待办支持完成、排序、计数和每周日程，便签支持自动保存、分行线和日夜皮肤。

## 使用

完整解压 Windows x64 免安装包后运行 `白术便签.exe`。任务、便签和设置保存在 `%LOCALAPPDATA%\Memo\state.json`，`state.json.bak` 保留上一份有效快照。也可以阅读[详细使用说明](src/Memo.Desktop/使用说明.md)。

## 构建和测试

需要 Windows 11、PowerShell 7 和 `global.json` 锁定的 .NET SDK。构建脚本优先使用配置的共享 SDK，也可以使用 PATH 上已安装的 SDK。

```powershell
.\scripts\build.ps1
dotnet run --project .\tests\Memo.Smoke\Memo.Smoke.csproj
```

生成自包含的 Windows x64 目录：

```powershell
.\scripts\build.ps1 -Publish
```

发布输出默认位于 `release\白术便签-win-x64`，可用 `-OutputDirectory` 更改。共享依赖目录可通过 `-DependencyRoot` 或 `CHATGPT_DEPENDENCE` 设置。若要强制使用 PATH 上的 SDK，可传入 `-UseInstalledSdk`。

## 源码

| 目录 | 职责 |
| --- | --- |
| `src/Memo.Desktop/Views` | WPF 桌面栏目与设置页面 |
| `src/Memo.Desktop/Models` | 任务、便签及设置的数据结构 |
| `src/Memo.Desktop/ViewModels` | 状态更新和保存流程 |
| `src/Memo.Desktop/Services` | JSON 存储、备份和恢复 |
| `src/Memo.Desktop/Windows` | 桌面层、托盘和全局快捷键 |
| `tests/Memo.Smoke` | 使用临时数据的冒烟回归 |

## 许可证

项目源码采用 [MIT 许可证](LICENSE)。自包含发行包的 `Runtime-Notices` 目录附带 .NET 运行时许可证和第三方声明。

[原创图标的 SVG 源文件](src/Memo.Desktop/Assets/BaizhuMemo.svg)随项目源码提供。图形取自白术根茎的肥厚分杈与纵沟。[湖北省农业科学院中药材研究所](https://www.hbzyc.com.cn/show_7_118_1.html)
