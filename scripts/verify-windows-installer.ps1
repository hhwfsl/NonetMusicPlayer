param([Parameter(Mandatory)][string]$Installer,[Parameter(Mandatory)][string]$FixturePackage)
$ErrorActionPreference='Stop'
# 安装器会检查运行中的播放器；测试前拒绝触碰其他目录的用户副本。
if (Get-Process Nonet -ErrorAction SilentlyContinue) { throw 'Close running Nonet copies before isolated installer QA.' }
$repository=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifacts=[IO.Path]::GetFullPath((Join-Path $repository 'artifacts'))
$work=Join-Path $artifacts ('installer-qa-'+[guid]::NewGuid().ToString('N'))
$installed=Join-Path $work 'app';$seed=Join-Path $work 'seed'
[IO.Directory]::CreateDirectory($work)|Out-Null
function Run([string]$Exe,[string[]]$Arguments){
 $start=[Diagnostics.ProcessStartInfo]::new($Exe);$start.UseShellExecute=$false;$start.CreateNoWindow=$true
 foreach($arg in $Arguments){$start.ArgumentList.Add($arg)}
 $taskProcess=[Diagnostics.Process]::Start($start)
 try{while(-not $taskProcess.WaitForExit(1000)){};if($taskProcess.ExitCode -ne 0){throw "Process failed: $Exe ($($taskProcess.ExitCode))"}}finally{$taskProcess.Dispose()}
}
$app=$null;$helper=$null;$previous=$env:NONET_DATA_DIR;$passed=$false
try{
 # 独立安装身份，不修改正式安装记录、不建立用户快捷方式。
 Run ([IO.Path]::GetFullPath($Installer)) @('/VERYSILENT','/NORESTART','/NOICONS','/SP-','/NonetInstallerQA=1',('/DIR='+$installed),('/LOG='+ (Join-Path $work 'setup.log')))
 $exe=Join-Path $installed 'Nonet.exe';$native=Join-Path $installed 'NonetCrashHandler.exe'
 if(-not(Test-Path -LiteralPath $native)){throw 'Installer did not install crash handler'}
 $expected=Join-Path $repository 'publish/desktop/windows/NonetCrashHandler.exe'
 if((Get-FileHash -LiteralPath $native).Hash -ne (Get-FileHash -LiteralPath $expected).Hash){throw 'Installed crash handler differs from published file'}
 # 真实发行程序先建立隔离测试数据：关闭托盘与退出确认，再走正常启动入口。
 Run $exe @('--verify-live-plugin-configuration',[IO.Path]::GetFullPath($FixturePackage),'configure',$seed)
 $env:NONET_DATA_DIR=Join-Path $seed 'data'
 $start=[Diagnostics.ProcessStartInfo]::new($exe);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WorkingDirectory=$installed
 $app=[Diagnostics.Process]::Start($start)
 $deadline=[Environment]::TickCount64+15000
 do{
  $helper=Get-Process NonetCrashHandler -ErrorAction SilentlyContinue|Where-Object{$_.Path -eq $native}|Select-Object -First 1
  if($helper){break};Start-Sleep -Milliseconds 100
 }while(-not $app.HasExited -and [Environment]::TickCount64 -lt $deadline)
 if(-not $helper){throw 'Normal installed startup did not launch native crash observer'}
 $observerPid=$helper.Id
 $deadline=[Environment]::TickCount64+15000
 do{$app.Refresh();if($app.MainWindowHandle -ne 0){break};Start-Sleep -Milliseconds 100}while(-not $app.HasExited -and [Environment]::TickCount64 -lt $deadline)
 if(-not $app.CloseMainWindow() -or -not $app.WaitForExit(20000)){throw 'Isolated player did not close normally'}
 if(-not $helper.WaitForExit(5000)){throw 'Crash observer remained after clean player shutdown'}
 if(Get-ChildItem (Join-Path $env:NONET_DATA_DIR 'Logs/Crashes') -File){throw 'Normal installed shutdown incorrectly reported a crash'}
 $passed=$true
 [IO.File]::WriteAllText((Join-Path $artifacts 'installer-crash-observer-result.txt'),"PASS`nInstalledHelper=True`nHashMatchesPublished=True`nNormalStartupObserverPID=$observerPid`nCleanExitStopsObserver=True`nNoCrashReportOnCleanExit=True`n")
 Write-Output 'PASS x64 installer payload, real normal startup observer, normal shutdown without crash report.'
}finally{
 $env:NONET_DATA_DIR=$previous
 # 故障清理仅限本次隔离安装创建的进程；不会关闭其他目录中的播放器。
 if($helper -and -not $helper.HasExited){$helper.Kill();$helper.WaitForExit(3000)|Out-Null}
 if($app -and -not $app.HasExited){$app.Kill();$app.WaitForExit(3000)|Out-Null}
 if($helper){$helper.Dispose()};if($app){$app.Dispose()}
 $uninstall=Join-Path $installed 'Uninstall/unins000.exe'
 if(Test-Path -LiteralPath $uninstall){Run $uninstall @('/VERYSILENT','/NORESTART')}
 if($passed){
  $resolved=[IO.Path]::GetFullPath($work)
  if(-not $resolved.StartsWith($artifacts+[IO.Path]::DirectorySeparatorChar+'installer-qa-',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe QA cleanup path'}
  if(Get-ChildItem -LiteralPath $resolved -Recurse -Force|Where-Object{$_.Attributes -band [IO.FileAttributes]::ReparsePoint}){throw 'Linked QA path'}
  Remove-Item -LiteralPath $resolved -Recurse -Force
 }
}