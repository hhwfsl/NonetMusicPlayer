param([Parameter(Mandatory)][string]$Zip, [Parameter(Mandatory)][string]$CliDirectory)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw '发行程序启动夹具仅在 Windows 验证。' }
$taskRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskProbe = Join-Path $taskRoot ('artifacts/zip-startup-' + [guid]::NewGuid().ToString('N'))
$taskPackage = Join-Path $taskProbe 'desktop'
$taskData = Join-Path $taskProbe 'desktop-data'
$taskCliData = Join-Path $taskProbe 'cli-data'
if (Get-Process -Name Nonet -ErrorAction SilentlyContinue) { throw '已有 Nonet 实例，跳过独立启动夹具，以免激活用户窗口。' }
Add-Type -AssemblyName System.IO.Compression
[IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($Zip), $taskPackage)
$taskProcess = Start-Process -FilePath (Join-Path $taskPackage 'Nonet.exe') -WorkingDirectory $taskPackage -Environment @{NONET_DATA_DIR=$taskData} -WindowStyle Hidden -PassThru
try {
    # 空音乐库不立即创建数据库，用户布局 JSON 已移除；以真实窗口日志和存活状态判断启动完成。
    $taskLogs = Join-Path $taskData 'Logs'
    $taskWindowShown = $null
    $taskDeadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    while ([DateTimeOffset]::UtcNow -lt $taskDeadline -and -not $taskProcess.HasExited -and -not $taskWindowShown) {
        if (Test-Path -LiteralPath $taskLogs) { $taskWindowShown = Get-ChildItem -LiteralPath $taskLogs -File | Select-String -SimpleMatch 'nonet window show' }
        if (-not $taskWindowShown) { Start-Sleep -Milliseconds 100 }
    }
    Start-Sleep -Milliseconds 800
    $taskStartupErrors = if (Test-Path -LiteralPath $taskLogs) { Get-ChildItem -LiteralPath $taskLogs -File | Select-String -SimpleMatch '[ERROR]' }
    if ($taskProcess.HasExited -or -not $taskWindowShown -or $taskStartupErrors) { throw 'ZIP 内发行程序未能完成首次启动或记录了启动错误。' }
    Write-Host 'PASS actual ZIP executable: starts with fresh isolated data, shows its window, remains alive and logs no startup errors.'
} finally {
    # 只结束本夹具创建并持有句柄的进程，不通过名称关闭任何用户实例。
    if (-not $taskProcess.HasExited) { $taskProcess.Kill(); $taskProcess.WaitForExit(5000) | Out-Null }
    $taskProcess.Dispose()
}
$taskCli = Join-Path ([IO.Path]::GetFullPath($CliDirectory)) 'nonet.exe'
$taskResult = & $taskCli --data $taskCliData --json player status
if ($LASTEXITCODE -ne 0) { throw '独立 CLI 发行程序启动失败。' }
$taskStatus = $taskResult | ConvertFrom-Json
if (-not $taskStatus.success) { throw 'CLI 发行程序的共享命令失败。' }
if (Get-ChildItem -LiteralPath ([IO.Path]::GetFullPath($CliDirectory)) -File | Where-Object Name -match 'Avalonia|Skia|HarfBuzz') { throw 'CLI 不应包含图形依赖。' }
Write-Host 'PASS published CLI: one-shot nonet command, Unicode JSON, isolated data and no GUI libraries.'
