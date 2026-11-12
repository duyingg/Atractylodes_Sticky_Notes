[CmdletBinding()]
param(
    [switch]$Publish,
    [switch]$UseInstalledSdk,
    [string]$DependencyRoot = $env:CHATGPT_DEPENDENCE,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$sdkVersion = (Get-Content -LiteralPath (Join-Path $repo 'global.json') -Raw | ConvertFrom-Json).sdk.version
$localSharedRoot = 'D:\ChatGPT\dependence'
$sdkPath = "dotnet\$sdkVersion-win-x64"
if (!$UseInstalledSdk -and !$DependencyRoot -and
    (Test-Path -LiteralPath (Join-Path $localSharedRoot "$sdkPath\dotnet.exe") -PathType Leaf)) {
    $DependencyRoot = $localSharedRoot
}

if (!$UseInstalledSdk -and $DependencyRoot) {
    $sdk = Join-Path $DependencyRoot $sdkPath
    $dotnet = Join-Path $sdk 'dotnet.exe'
    if (!(Test-Path -LiteralPath $dotnet -PathType Leaf)) {
        throw "找不到共享 .NET SDK：$dotnet"
    }
    $cache = Join-Path $DependencyRoot 'cache\projects\memo'
    $env:DOTNET_ROOT = $sdk
    $env:DOTNET_CLI_HOME = Join-Path $cache 'cli'
    $env:NUGET_PACKAGES = Join-Path $DependencyRoot 'cache\nuget\packages'
    $artifacts = Join-Path $cache 'artifacts'
} else {
    $dotnet = (Get-Command dotnet.exe -CommandType Application | Select-Object -First 1).Source
    $artifacts = Join-Path $repo '.artifacts'
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$project = Join-Path $repo 'src\Memo.Desktop\Memo.Desktop.csproj'
Push-Location -LiteralPath $repo
try {
    if ($Publish) {
        $projectFile = [xml](Get-Content -LiteralPath $project -Raw -Encoding utf8)
        $applicationName = $projectFile.Project.PropertyGroup.AssemblyName
        $release = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory, $repo) } else { Join-Path $repo "release\$applicationName-win-x64" }
        & $dotnet publish $project -c Release -r win-x64 --self-contained true --artifacts-path $artifacts -o $release
        if ($LASTEXITCODE -ne 0) { throw "发布失败，退出码 $LASTEXITCODE" }
        $config = Get-Content -LiteralPath (Join-Path $release "$applicationName.runtimeconfig.json") -Raw | ConvertFrom-Json
        $frameworks = $config.runtimeOptions.includedFrameworks
        $coreVersion = ($frameworks | Where-Object name -eq 'Microsoft.NETCore.App').version
        $desktopVersion = ($frameworks | Where-Object name -eq 'Microsoft.WindowsDesktop.App').version
        $packages = $env:NUGET_PACKAGES
        if (!$packages) {
            $listing = & $dotnet nuget locals global-packages --list
            if ($LASTEXITCODE -ne 0 -or $listing -notmatch '^[^:]+:\s*(.+)$') {
                throw '无法定位 NuGet 全局缓存。'
            }
            $packages = $Matches[1]
        }
        $corePack = Join-Path $packages "microsoft.netcore.app.runtime.win-x64\$coreVersion"
        $desktopPack = Join-Path $packages "microsoft.windowsdesktop.app.runtime.win-x64\$desktopVersion"
        $notices = New-Item -ItemType Directory -Force -Path (Join-Path $release 'Runtime-Notices')
        Copy-Item -LiteralPath (Join-Path $corePack 'LICENSE.TXT') -Destination (Join-Path $notices.FullName 'NETCore-LICENSE.txt')
        Copy-Item -LiteralPath (Join-Path $corePack 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $notices.FullName 'NETCore-THIRD-PARTY-NOTICES.txt')
        Copy-Item -LiteralPath (Join-Path $desktopPack 'LICENSE') -Destination (Join-Path $notices.FullName 'WindowsDesktop-LICENSE.txt')
    } else {
        & $dotnet build $project -c Debug --artifacts-path $artifacts
    }
    if ($LASTEXITCODE -ne 0) { throw "构建失败，退出码 $LASTEXITCODE" }
} finally {
    Pop-Location
}
