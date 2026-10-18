param([string]$CompilerPath, [string]$Version = '', [string]$PackageDirectory, [switch]$CheckCompilerOnly)
$ErrorActionPreference = 'Stop'
$taskRepository = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskProject = ([xml](Get-Content -Raw -LiteralPath (Join-Path $taskRepository 'src/NonetMusicPlayer.Desktop/NonetMusicPlayer.Desktop.csproj'))).Project.PropertyGroup
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $taskProject.Version }
if ($Version -ne $taskProject.Version -or $Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw '安装包版本必须与当前桌面项目版本一致。' }
if ([string]::IsNullOrWhiteSpace($CompilerPath)) {
    $taskCandidates = @(
        (Join-Path $taskRepository 'artifacts/toolchain/inno-6.7.3/package/tools/ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )
    $CompilerPath = $taskCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($CompilerPath) -or -not (Test-Path -LiteralPath $CompilerPath)) { throw 'Specify -CompilerPath pointing to the Inno Setup compiler.' }
if ($CheckCompilerOnly) { Write-Host "Installer compiler: $CompilerPath"; return }
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) { $PackageDirectory = Join-Path $taskRepository 'publish/desktop/windows' }
$taskPackage = [IO.Path]::GetFullPath($PackageDirectory)
$taskExecutable = Join-Path $taskPackage 'Nonet.exe'
if (-not (Test-Path -LiteralPath $taskExecutable -PathType Leaf)) { throw 'Publish the Windows application before building its installer.' }
# 不允许把旧程序重标成新版本；纯净构建也可以直接指定其 package 目录。
$taskBinaryVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($taskExecutable)
if ($taskBinaryVersion.ProductVersion.Split('+')[0] -ne $Version -or [version]$taskBinaryVersion.FileVersion -ne [version]$taskProject.FileVersion) {
    throw '待打包程序版本与项目不一致，请先重新发布 Windows 程序。'
}
$taskOutput = [IO.Path]::GetFullPath((Join-Path $taskRepository 'publish/desktop/windows_installer'))
$taskArtifacts = [IO.Path]::GetFullPath((Join-Path $taskRepository 'artifacts'))
$taskAllowed = $taskArtifacts.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$taskToken = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N')
$taskStage = [IO.Path]::GetFullPath((Join-Path $taskArtifacts ('installer-stage-' + $taskToken)))
$taskArchive = [IO.Path]::GetFullPath((Join-Path $taskArtifacts ('releases/installers/' + $taskToken)))
if (-not $taskStage.StartsWith($taskAllowed, [StringComparison]::OrdinalIgnoreCase) -or -not $taskArchive.StartsWith($taskAllowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe installer path.' }
[IO.Directory]::CreateDirectory($taskArtifacts) | Out-Null
# 排他锁避免两个发布任务同时清理或替换安装包；编译失败不触碰现有安装器。
$taskLock = [IO.File]::Open((Join-Path $taskArtifacts 'installer-build.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    [IO.Directory]::CreateDirectory($taskStage) | Out-Null
    [IO.Directory]::CreateDirectory($taskOutput) | Out-Null
    & $CompilerPath "/O$taskStage" "/DAppVersion=$Version" "/DAppFileVersion=$($taskProject.FileVersion)" "/DRepositoryRoot=$taskRepository" "/DPackageDirectory=$taskPackage" (Join-Path $PSScriptRoot 'windows-installer/NonetMusicPlayer.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Windows installer build failed; existing installer preserved.' }
    $taskCurrentName = "NonetMusicPlayer-$Version-windows-x64-setup.exe"
    $taskBuilt = Join-Path $taskStage $taskCurrentName
    if (-not (Test-Path -LiteralPath $taskBuilt -PathType Leaf) -or (Get-Item -LiteralPath $taskBuilt).Length -eq 0 -or [version]([Diagnostics.FileVersionInfo]::GetVersionInfo($taskBuilt).FileVersion) -ne [version]$taskProject.FileVersion) { throw 'Installer output validation failed; existing installer preserved.' }
    $taskOldFiles = @(Get-ChildItem -LiteralPath $taskOutput -File -Filter 'NonetMusicPlayer-*-windows-x64-setup.exe')
    foreach ($taskOld in $taskOldFiles) {
        if (($taskOld.Attributes -band [IO.FileAttributes]::ReparsePoint) -or [IO.Path]::GetFullPath($taskOld.DirectoryName) -ne $taskOutput) { throw 'Unsafe installer source path.' }
    }
    $taskMoved = [Collections.Generic.List[string]]::new()
    try {
        foreach ($taskOld in $taskOldFiles) {
            [IO.Directory]::CreateDirectory($taskArchive) | Out-Null
            Move-Item -LiteralPath $taskOld.FullName -Destination (Join-Path $taskArchive $taskOld.Name)
            $taskMoved.Add($taskOld.Name)
        }
        Move-Item -LiteralPath $taskBuilt -Destination (Join-Path $taskOutput $taskCurrentName)
    } catch {
        # 只恢复本次确实移动的文件；不覆盖外部程序新写入的内容。
        foreach ($taskName in $taskMoved) {
            if (-not (Test-Path -LiteralPath (Join-Path $taskOutput $taskName))) { Move-Item -LiteralPath (Join-Path $taskArchive $taskName) -Destination (Join-Path $taskOutput $taskName) }
        }
        throw
    }
    Write-Host "Latest installer: $(Join-Path $taskOutput $taskCurrentName)"
    if ($taskMoved.Count) { Write-Host "Previous installers archived: $taskArchive ($($taskMoved.Count) files)" }
} finally {
    $taskLock.Dispose()
    # 仅移除已确认位于 artifacts 下且为空的编译暂存目录，不递归删除。
    if ((Test-Path -LiteralPath $taskStage) -and -not (Get-ChildItem -LiteralPath $taskStage -Force)) { Remove-Item -LiteralPath $taskStage }
}
