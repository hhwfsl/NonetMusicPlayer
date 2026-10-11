param([Parameter(Mandatory)][string]$PackageDirectory, [Parameter(Mandatory)][string]$Rid, [Parameter(Mandatory)][string]$Version, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskRepository = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskPackage = [IO.Path]::GetFullPath($PackageDirectory)
$taskAllowedRoot = [IO.Path]::GetFullPath((Join-Path $taskRepository 'artifacts')).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if (-not $taskPackage.StartsWith($taskAllowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Only clean staging packages under artifacts may be packaged.' }
$taskSource = if ($Rid.StartsWith('osx-')) { Join-Path $taskPackage 'Nonet.app' } else { $taskPackage }
$taskOutput = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $taskRepository "artifacts/releases/update-packages/$Version" }
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskZip = Join-Path $taskOutput "NonetMusicPlayer.Desktop-$Version-$Rid.zip"
$taskTemporary = $taskZip + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
$taskFiles = [ordered]@{}
$taskBlocked = @('Data', 'Plugins', 'Artwork', 'Lyrics', 'Fonts', 'Backups', 'Logs', 'Backgrounds', 'Updates', 'Nonet.bootstrap.json')
Add-Type -AssemblyName System.IO.Compression
try {
    $taskStream = [IO.File]::Open($taskTemporary, [IO.FileMode]::CreateNew)
    $taskArchive = [IO.Compression.ZipArchive]::new($taskStream, [IO.Compression.ZipArchiveMode]::Create, $false, [Text.Encoding]::UTF8)
    try {
        foreach ($taskFile in Get-ChildItem -LiteralPath $taskSource -File -Recurse) {
            $taskRelative = [IO.Path]::GetRelativePath($taskSource, $taskFile.FullName).Replace('\', '/')
            if (($taskFile.Attributes -band [IO.FileAttributes]::ReparsePoint) -or ($taskRelative.Split('/') | Where-Object { $_ -in $taskBlocked })) { throw "User data or linked file in clean package: $taskRelative" }
            $taskInput = [IO.File]::OpenRead($taskFile.FullName)
            try { $taskFiles[$taskRelative] = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($taskInput)).ToLowerInvariant(); $taskInput.Position = 0
                $taskEntry = $taskArchive.CreateEntry($taskRelative, [IO.Compression.CompressionLevel]::Fastest)
                # POSIX 模式用于跨平台恢复可执行权限；普通文档和依赖采用 0644。
                $taskMode = if ($taskFile.Name -eq 'Nonet') { 0x81ED } else { 0x81A4 }
                $taskEntry.ExternalAttributes = [int]($taskMode -shl 16)
                $taskWriter = $taskEntry.Open(); try { $taskInput.CopyTo($taskWriter) } finally { $taskWriter.Dispose() }
            } finally { $taskInput.Dispose() }
        }
        $taskManifest = @{ schema = 1; version = $Version; rid = $Rid; files = $taskFiles } | ConvertTo-Json -Depth 5
        $taskManifestEntry = $taskArchive.CreateEntry('NonetMusicPlayer.update.json')
        $taskWriter = [IO.StreamWriter]::new($taskManifestEntry.Open(), [Text.UTF8Encoding]::new($false))
        try { $taskWriter.Write($taskManifest) } finally { $taskWriter.Dispose() }
    } finally { $taskArchive.Dispose(); $taskStream.Dispose() }
    [IO.File]::Move($taskTemporary, $taskZip, $true)
    Write-Host "Verified update package: $taskZip"
} finally { if (Test-Path -LiteralPath $taskTemporary) { Remove-Item -LiteralPath $taskTemporary -Force } }
