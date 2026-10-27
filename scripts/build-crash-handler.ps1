param([ValidateSet('win-x64','win-x86','win-arm64','linux-x64','linux-arm64','osx-x64')][string[]]$Rids=@('win-x64'),[string]$Zig='')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(-not $Zig){$Zig=$env:NONET_ZIG}
if(-not $Zig -and (Get-Command zig -ErrorAction SilentlyContinue)){$Zig=(Get-Command zig).Source}
if(-not $Zig){$Zig=Join-Path $root 'artifacts/toolchain/zig-x86_64-windows-0.15.2/zig.exe'}
if(-not(Test-Path -LiteralPath $Zig)){throw 'Set -Zig to Zig 0.15.2 or newer. See native/NonetCrashHandler/README.md.'}
$env:ZIG_GLOBAL_CACHE_DIR=Join-Path $root 'artifacts/native-cache/zig-global'
$env:ZIG_LOCAL_CACHE_DIR=Join-Path $root 'artifacts/native-cache/zig-local'
$source=Join-Path $root 'native/NonetCrashHandler/crash_handler.cpp'
foreach($rid in $Rids){
 $target=switch($rid){'win-x64'{'x86_64-windows-gnu'} 'win-x86'{'x86-windows-gnu'} 'win-arm64'{'aarch64-windows-gnu'} 'linux-x64'{'x86_64-linux-musl'} 'linux-arm64'{'aarch64-linux-musl'} 'osx-x64'{'x86_64-macos.12.0-none'}}
 $output=Join-Path $root "artifacts/native/NonetCrashHandler/$rid"
 New-Item -ItemType Directory -Force -Path $output | Out-Null
 $name=if($rid.StartsWith('win-')){'NonetCrashHandler.exe'}else{'NonetCrashHandler'}
 # 只编译 C++，不引入 C++ 标准库；Linux 静态链接 musl，Windows 使用系统 API。
 $compilerArgs=@('cc','-x','c++','-std=c++17','-Os','-fno-exceptions','-fno-rtti','-fno-unwind-tables','-fno-asynchronous-unwind-tables','-s','-target',$target,$source,'-o',(Join-Path $output $name))
 if($rid.StartsWith('win-')){$compilerArgs+=@('-municode')}
 if($rid.StartsWith('linux-')){$compilerArgs+=@('-static')}
 & $Zig @compilerArgs
 if($LASTEXITCODE -ne 0){throw "Crash handler compile failed: $rid"}
 Get-Item -LiteralPath (Join-Path $output $name) | Select-Object FullName,Length
}
