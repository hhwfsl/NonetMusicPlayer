param([Parameter(Mandatory)][string]$Directory, [Parameter(Mandatory)][ValidateSet('win-x64','win-x86','win-arm64','linux-x64','linux-arm64','osx-x64')][string]$Rid)
$ErrorActionPreference = 'Stop'
$taskDirectory = [IO.Path]::GetFullPath($Directory)
$taskArchitecture = $Rid.Split('-')[1]
function Read-UInt32BE([byte[]]$Bytes, [int]$Offset) {
    $part = [byte[]]$Bytes[$Offset..($Offset+3)]; [Array]::Reverse($part)
    return [BitConverter]::ToUInt32($part,0)
}
function Get-BinaryArchitectures([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        $header = $reader.ReadBytes(64)
        if ($header.Length -lt 64) { throw "二进制头不完整：$Path" }
        if ($header[0] -eq 0x4D -and $header[1] -eq 0x5A) {
            $offset = [BitConverter]::ToUInt32($header,60)
            if ($offset -gt $stream.Length-24) { throw "无效 PE 偏移：$Path" }
            $stream.Position = $offset
            if ($reader.ReadUInt32() -ne 0x4550) { throw "无效 PE 签名：$Path" }
            $architecture = switch ($reader.ReadUInt16()) { 0x014C {'x86'} 0x8664 {'x64'} 0xAA64 {'arm64'} default {throw "不支持的 PE 架构：$Path"} }
            return $architecture
        }
        if ($header[0] -eq 0x7F -and [Text.Encoding]::ASCII.GetString($header,1,3) -eq 'ELF') {
            if ($header[4] -ne 2 -or $header[5] -ne 1) { throw "仅支持目标所需的 64 位小端 ELF：$Path" }
            $architecture = switch ([BitConverter]::ToUInt16($header,18)) { 62 {'x64'} 183 {'arm64'} default {throw "不支持的 ELF 架构：$Path"} }
            return $architecture
        }
        $magic = [BitConverter]::ToString($header,0,4)
        if ($magic -in @('CF-FA-ED-FE','FE-ED-FA-CF')) {
            $cpu = if ($magic -eq 'CF-FA-ED-FE') {[BitConverter]::ToUInt32($header,4)} else {Read-UInt32BE $header 4}
            $architecture = switch ($cpu) { 0x01000007 {'x64'} 0x0100000C {'arm64'} default {throw "不支持的 Mach-O 架构：$Path"} }
            return $architecture
        }
        # Apple 的依赖可能采用通用二进制；必须确实包含目标切片，不能只看文件名。
        if ($magic -in @('CA-FE-BA-BE','CA-FE-BA-BF')) {
            $count = Read-UInt32BE $header 4
            if ($count -lt 1 -or $count -gt 32) { throw "无效 Mach-O 切片数量：$Path" }
            $size = if ($magic -eq 'CA-FE-BA-BF') {32} else {20}
            $stream.Position = 8; $entries = $reader.ReadBytes($count*$size)
            if ($entries.Length -ne $count*$size) {throw "Mach-O 切片表不完整：$Path"}
            for ($i=0; $i -lt $count; $i++) {
                switch (Read-UInt32BE $entries ($i*$size)) {0x01000007 {'x64'} 0x0100000C {'arm64'}}
            }
            return
        }
        throw "未识别的原生文件：$Path"
    } finally {$reader.Dispose();$stream.Dispose()}
}
$taskRequired = if ($Rid.StartsWith('win-')) {
    @('Nonet.exe','e_sqlite3.dll','libHarfBuzzSharp.dll','libSkiaSharp.dll','miniaudio.dll','soundflow-ffmpeg.dll')
} elseif ($Rid.StartsWith('linux-')) {
    @('Nonet','libe_sqlite3.so','libHarfBuzzSharp.so','libSkiaSharp.so','libminiaudio.so','libsoundflow-ffmpeg.so')
} else {
    @('Nonet','libAvaloniaNative.dylib','libe_sqlite3.dylib','libHarfBuzzSharp.dylib','libSkiaSharp.dylib','libminiaudio.dylib','libsoundflow-ffmpeg.dylib')
}
foreach ($name in $taskRequired) {
    $path = Join-Path $taskDirectory $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) {throw "缺少或链接了必需原生文件：$path"}
    $architectures = @(Get-BinaryArchitectures $path)
    if ($taskArchitecture -notin $architectures) {throw "架构不匹配：$name 是 $($architectures -join ',')，要求 $taskArchitecture"}
}
Write-Host "PASS native $Rid : executable and $($taskRequired.Count-1) native dependencies"
