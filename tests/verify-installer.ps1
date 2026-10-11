param([Parameter(Mandatory)][string]$Installer, [Parameter(Mandatory)][string]$Zip)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw '安装器夹具仅在 Windows 执行。' }
$taskRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskStage = Join-Path $taskRoot ('artifacts/installer-check-' + [guid]::NewGuid().ToString('N'))
$taskAllowed = [IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts')).TrimEnd('\') + '\'
if (-not [IO.Path]::GetFullPath($taskStage).StartsWith($taskAllowed, [StringComparison]::OrdinalIgnoreCase)) { throw '夹具目录越界。' }
if (Get-Process -Name Nonet -ErrorAction SilentlyContinue) { throw '先关闭 Nonet，再执行隔离安装器检查。' }
# 模拟 beta.16 的解码器残留，只删除已知发行文件，未知用户文件应当保留。
$taskRetired = Join-Path $taskStage 'VideoDecoder'
[IO.Directory]::CreateDirectory($taskRetired) | Out-Null
foreach ($taskName in @('ffmpeg.exe','ffmpeg','LICENSE.txt','NOTICE.txt')) { [IO.File]::WriteAllText((Join-Path $taskRetired $taskName),'obsolete fixture') }
[IO.File]::WriteAllText((Join-Path $taskRetired 'keep.txt'),'unknown user fixture')
$taskArgs = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/LMPQA=1',('/DIR="' + $taskStage + '"'))
$taskProcess = Start-Process -FilePath ([IO.Path]::GetFullPath($Installer)) -ArgumentList $taskArgs -WindowStyle Hidden -Wait -PassThru
if ($taskProcess.ExitCode -ne 0) { throw '隔离安装失败。' }
foreach ($taskName in @('ffmpeg.exe','ffmpeg','LICENSE.txt','NOTICE.txt')) { if (Test-Path -LiteralPath (Join-Path $taskRetired $taskName)) { throw '安装器未清理旧背景解码器。' } }
if (-not (Test-Path -LiteralPath (Join-Path $taskRetired 'keep.txt'))) { throw '安装器不应删除旧目录中的未知文件。' }
Write-Host 'PASS installer migration: retired decoder removed, unknown file retained.'
try {
    Add-Type -AssemblyName System.IO.Compression
    $taskArchive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Zip))
    try {
        $taskReader = [IO.StreamReader]::new($taskArchive.GetEntry('NonetMusicPlayer.update.json').Open())
        try { $taskManifest = $taskReader.ReadToEnd() | ConvertFrom-Json } finally { $taskReader.Dispose() }
        foreach ($taskEntry in $taskManifest.files.PSObject.Properties) {
            $taskTarget = [IO.Path]::GetFullPath((Join-Path $taskStage $taskEntry.Name))
            if (-not $taskTarget.StartsWith($taskAllowed, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $taskTarget) -or (Get-FileHash -LiteralPath $taskTarget -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskEntry.Value) { throw '安装器与纯净 ZIP 的程序文件不一致。' }
        }
        Write-Host ('PASS installer: ' + @($taskManifest.files.PSObject.Properties).Count + ' program files match ZIP SHA-256.')
    } finally { $taskArchive.Dispose() }
    # 生成独立用户数据夹具，检查卸载只删除程序；不读取任何实际用户数据。
    [IO.Directory]::CreateDirectory((Join-Path $taskStage 'Data')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $taskStage 'Data/keep.txt'),'isolated-fixture')
} finally {
    $taskUninstaller = Join-Path $taskStage 'Uninstall/unins000.exe'
    if (Test-Path -LiteralPath $taskUninstaller) {
        $taskProcess = Start-Process -FilePath $taskUninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -Wait -PassThru
        if ($taskProcess.ExitCode -ne 0) { throw '隔离卸载失败。' }
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $taskStage 'Data/keep.txt')) -or (Test-Path -LiteralPath (Join-Path $taskStage 'Nonet.exe'))) { throw '卸载未保留数据或未移除程序。' }
Write-Host 'PASS isolated uninstall: program removed, non-program fixture data retained.'
