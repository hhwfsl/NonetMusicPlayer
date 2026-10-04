$ErrorActionPreference = 'Stop'
$taskPluginRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$taskPackageRoot = Join-Path $taskPluginRoot 'package'
$taskOutput = Join-Path $taskPluginRoot 'dist/sample.lyrics-timing.impp'
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($taskOutput)) | Out-Null
Add-Type -AssemblyName System.IO.Compression
# 创建临时包再原子替换，避免打包失败破坏已可导入的版本。
$taskStage = Join-Path $taskPluginRoot ('dist/' + [Guid]::NewGuid().ToString('N') + '.impp')
try {
    $taskZip = [IO.Compression.ZipFile]::Open($taskStage, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($taskFile in @('manifest.json', 'page.json')) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskZip, (Join-Path $taskPackageRoot $taskFile), $taskFile, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $taskZip.Dispose() }
    [IO.File]::Move($taskStage, $taskOutput, $true)
} finally { if ([IO.File]::Exists($taskStage)) { [IO.File]::Delete($taskStage) } }
Write-Host $taskOutput
