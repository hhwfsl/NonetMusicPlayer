param([string]$Directory, [string]$Version = '0.4.0-beta.1', [ValidateSet('win-x64','win-x86','win-arm64','osx-x64','linux-x64','linux-arm64')][string[]]$Rids = @('win-x64','win-x86','win-arm64','osx-x64','linux-x64','linux-arm64'))
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if (-not $Directory) { $Directory = Join-Path $taskRoot 'publish/desktop/archives' }
Add-Type -AssemblyName System.IO.Compression
foreach ($taskRid in $Rids) {
    $taskPath = Join-Path $Directory "NonetMusicPlayer.Desktop-$Version-$taskRid.zip"
    $taskZip = [IO.Compression.ZipFile]::OpenRead($taskPath)
    try {
        $taskManifestEntry = $taskZip.GetEntry('NonetMusicPlayer.update.json')
        if (-not $taskManifestEntry) { throw '缺少更新清单。' }
        $taskReader = [IO.StreamReader]::new($taskManifestEntry.Open())
        try { $taskManifest = $taskReader.ReadToEnd() | ConvertFrom-Json } finally { $taskReader.Dispose() }
        if ($taskManifest.schema -ne 1 -or $taskManifest.version -ne $Version -or $taskManifest.rid -ne $taskRid) { throw '更新清单身份不匹配。' }
        $taskSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($taskEntry in $taskZip.Entries) {
            if ($taskEntry.FullName -eq 'NonetMusicPlayer.update.json') { continue }
            if (-not $taskSeen.Add($taskEntry.FullName) -or $taskEntry.FullName -match '(^|/)(Data|Logs|Lyrics|Artwork|Fonts|Plugins|Backgrounds|Backups|Updates|bin|obj)/|\.bootstrap\.json$|\.(pdb|deps\.json)$') { throw "非纯净文件或重复路径：$($taskEntry.FullName)" }
            $taskDeclared = $taskManifest.files.PSObject.Properties[$taskEntry.FullName]
            if (-not $taskDeclared) { throw "清单遗漏：$($taskEntry.FullName)" }
            $taskStream = $taskEntry.Open()
            try { $taskHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($taskStream)).ToLowerInvariant() } finally { $taskStream.Dispose() }
            if ($taskHash -ne $taskDeclared.Value) { throw "校验不一致：$($taskEntry.FullName)" }
        }
        if ($taskSeen.Count -ne @($taskManifest.files.PSObject.Properties).Count) { throw '清单文件缺失。' }
        $taskExe = if ($taskRid.StartsWith('win-')) { 'Nonet.exe' } elseif ($taskRid.StartsWith('osx-')) { 'Contents/MacOS/Nonet' } else { 'Nonet' }
        $taskLicense = if ($taskRid.StartsWith('osx-')) { 'Contents/MacOS/LICENSE' } else { 'LICENSE' }
        if (-not $taskSeen.Contains($taskExe) -or -not $taskSeen.Contains($taskLicense)) { throw '缺少 Nonet 可执行文件或 GPL 许可证。' }
        if (-not $taskRid.StartsWith('win-') -and ($taskZip.GetEntry($taskExe).ExternalAttributes -shr 16 -band 0x1FF) -ne 0x1ED) { throw '缺少 POSIX 可执行权限。' }
        if ($taskSeen | Where-Object { $_ -match '(^|/)VideoDecoder/|(^|/)ffmpeg(\.exe)?$|BackgroundDecoder' }) { throw '发布包不应包含已移除的视频背景解码器。' }
        $taskReader = [IO.StreamReader]::new($taskZip.GetEntry($taskLicense).Open())
        try { if ($taskReader.ReadToEnd() -notmatch 'Version 3, 29 June 2007') { throw 'GPL 许可内容不完整。' } } finally { $taskReader.Dispose() }
        Write-Host "PASS clean $taskRid : $($taskSeen.Count) required files, $([math]::Round((Get-Item -LiteralPath $taskPath).Length / 1MB,2)) MiB, full SHA-256 and executable permissions"
    } finally { $taskZip.Dispose() }
}
