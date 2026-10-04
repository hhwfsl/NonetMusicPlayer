param([string]$CompilerPath, [string]$Version = '')
$ErrorActionPreference = 'Stop'
$taskRepository = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
# 单独构建安装程序也读取项目版本，避免安装包标识落后于播放器。
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = ([xml](Get-Content -Raw -LiteralPath (Join-Path $taskRepository 'src/NonetMusicPlayer.Desktop/NonetMusicPlayer.Desktop.csproj'))).Project.PropertyGroup.Version }
if ([string]::IsNullOrWhiteSpace($CompilerPath)) {
    $taskCandidates = @(
        (Join-Path $taskRepository 'artifacts/toolchain/inno-6.7.3/package/tools/ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )
    $CompilerPath = $taskCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($CompilerPath) -or -not (Test-Path -LiteralPath $CompilerPath)) { throw 'Specify -CompilerPath pointing to the Inno Setup compiler.' }
$taskPackage = Join-Path $taskRepository 'publish/desktop/windows'
if (-not (Test-Path -LiteralPath (Join-Path $taskPackage 'Nonet.exe'))) { throw 'Publish the Windows application before building its installer.' }
$taskOutput = Join-Path $taskRepository 'publish/desktop/windows_installer'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
& $CompilerPath "/DAppVersion=$Version" "/DRepositoryRoot=$taskRepository" "/DPackageDirectory=$taskPackage" (Join-Path $PSScriptRoot 'windows-installer/NonetMusicPlayer.iss')
if ($LASTEXITCODE -ne 0) { throw 'Windows installer build failed.' }
$taskCurrentName = "NonetMusicPlayer-$Version-windows-x64-setup.exe"
$taskArchive = [IO.Path]::GetFullPath((Join-Path $taskRepository ('artifacts/releases/installers/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))))
$taskArchivePrefix = [IO.Path]::GetFullPath((Join-Path $taskRepository 'artifacts')).TrimEnd('\') + '\'
if (-not $taskArchive.StartsWith($taskArchivePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe installer archive path.' }
foreach ($taskOldInstaller in Get-ChildItem -LiteralPath $taskOutput -File -Filter 'NonetMusicPlayer-*-windows-x64-setup.exe') {
    if ($taskOldInstaller.Name -eq $taskCurrentName -or ($taskOldInstaller.Attributes -band [IO.FileAttributes]::ReparsePoint)) { continue }
    if ([IO.Path]::GetFullPath($taskOldInstaller.DirectoryName) -ne [IO.Path]::GetFullPath($taskOutput)) { throw 'Unsafe installer source path.' }
    [IO.Directory]::CreateDirectory($taskArchive) | Out-Null
    Move-Item -LiteralPath $taskOldInstaller.FullName -Destination (Join-Path $taskArchive $taskOldInstaller.Name)
}
Write-Host "Installer available in $taskOutput"
