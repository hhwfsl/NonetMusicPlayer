param([string[]]$Rids = @('win-x64','linux-x64','osx-x64'))
$ErrorActionPreference = 'Stop'
$taskRepository = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskManifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'background-decoder.json') -Raw | ConvertFrom-Json
foreach ($taskRid in $Rids) {
    $taskPlatform = $taskManifest.platforms.PSObject.Properties[$taskRid].Value
    if (-not $taskPlatform) { throw 'Unsupported background decoder platform.' }
    $taskCache = Join-Path $taskRepository "artifacts/video-decoder/$taskRid"
    [IO.Directory]::CreateDirectory($taskCache) | Out-Null
    $taskArchive = Join-Path $taskCache $taskPlatform.asset
    if (-not (Test-Path -LiteralPath $taskArchive)) {
        $taskPartial = $taskArchive + '.partial'
        try { Invoke-WebRequest -Uri "https://github.com/$($taskManifest.repository)/releases/download/$($taskManifest.tag)/$($taskPlatform.asset)" -OutFile $taskPartial; Move-Item -LiteralPath $taskPartial -Destination $taskArchive }
        finally { if (Test-Path -LiteralPath $taskPartial) { Remove-Item -LiteralPath $taskPartial } }
    }
    if ((Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskPlatform.sha256) { throw "Decoder checksum mismatch: $taskRid" }
    $taskMembers = @(tar -tf $taskArchive)
    if ($LASTEXITCODE -ne 0) { throw 'Could not read decoder archive.' }
    # 只提取已校验归档中的唯一解码器，绝不解压其他工具、目录或链接。
    $taskMember = @($taskMembers | Where-Object { $_ -eq $taskPlatform.file -or $_ -eq ('./'+$taskPlatform.file) })
    if ($taskMember.Count -ne 1) { throw 'Unexpected decoder archive layout.' }
    tar -xf $taskArchive -C $taskCache $taskMember[0]
    if ($LASTEXITCODE -ne 0) { throw 'Decoder extraction failed.' }
    $taskExe = Join-Path $taskCache $taskPlatform.file
    if (-not (Test-Path -LiteralPath $taskExe) -or ((Get-Item -LiteralPath $taskExe).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Invalid decoder executable.' }
    Copy-Item -LiteralPath (Join-Path $taskRepository 'docs/licenses/LGPL-2.1.txt') -Destination (Join-Path $taskCache 'LICENSE.txt')
    $taskNotice = "FFmpeg $($taskManifest.version), LGPL-2.1-or-later; no modifications.`nBinary/build source: https://github.com/$($taskManifest.repository)/tree/$($taskManifest.tag)`nCorresponding FFmpeg source: $($taskManifest.source)`nArchive SHA256: $($taskPlatform.sha256)`nReplaceable standalone decoder; no audio output is used by Nonet.`n"
    $taskNotice += "`n" + [IO.File]::ReadAllText((Join-Path $taskRepository 'docs/licenses/BackgroundDecoder-notices.txt'))
    [IO.File]::WriteAllText((Join-Path $taskCache 'NOTICE.txt'), $taskNotice, [Text.UTF8Encoding]::new($false))
    Write-Host "Verified background decoder: $taskRid $taskExe"
}
