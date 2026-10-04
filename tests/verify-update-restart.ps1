param([string]$PublishedDirectory = 'publish/desktop/windows')
$ErrorActionPreference = 'Stop'
$taskRepository = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskPublished = [IO.Path]::GetFullPath((Join-Path $taskRepository $PublishedDirectory))
if (-not $IsWindows) { throw 'This cross-process fixture is Windows-only.' }
$taskRoot = Join-Path $taskRepository ('artifacts/update-restart-' + [guid]::NewGuid().ToString('N'))
$taskInstall = Join-Path $taskRoot 'installation'
$taskStage = Join-Path $taskRoot 'stage'
$taskPayload = Join-Path $taskStage 'payload'
$taskHelper = Join-Path $taskStage 'helper'
$taskData = Join-Path $taskInstall 'Data'
foreach ($taskDirectory in @($taskInstall,$taskPayload,$taskHelper,$taskData)) { [IO.Directory]::CreateDirectory($taskDirectory) | Out-Null }
dotnet build (Join-Path $PSScriptRoot 'NonetMusicPlayer.UpdateRestartFixture/NonetMusicPlayer.UpdateRestartFixture.csproj') -c Release -o $taskPayload --nologo
if ($LASTEXITCODE -ne 0) { throw 'Update fixture build failed.' }
[IO.File]::WriteAllText((Join-Path $taskPayload 'notes.txt'), 'updated application notes')
$taskFiles = [ordered]@{}
foreach ($taskFile in Get-ChildItem -LiteralPath $taskPayload -File) {
    if ($taskFile.Extension -eq '.pdb') { continue }
    $taskFiles[$taskFile.Name] = (Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    Copy-Item -LiteralPath $taskFile.FullName -Destination (Join-Path $taskInstall $taskFile.Name)
}
foreach ($taskFile in Get-ChildItem -LiteralPath $taskPublished -File) {
    if ($taskFile.Name -eq 'Nonet.exe' -or $taskFile.Extension -eq '.dll') { Copy-Item -LiteralPath $taskFile.FullName -Destination (Join-Path $taskHelper $taskFile.Name) }
}
[IO.File]::WriteAllText((Join-Path $taskData 'keep.txt'), 'original user data')
[IO.File]::WriteAllText((Join-Path $taskInstall 'notes.txt'), 'obsolete application notes')
# 旧替身先运行，真正的发行程序助手等待该 PID 退出，再替换并启动新替身。
$taskParent = Start-Process -FilePath (Join-Path $taskInstall 'Nonet.exe') -ArgumentList 'wait' -WindowStyle Hidden -PassThru
$taskDeadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
while (-not (Test-Path -LiteralPath (Join-Path $taskInstall 'parent-ready.txt')) -and [DateTimeOffset]::UtcNow -lt $taskDeadline) { Start-Sleep -Milliseconds 50 }
if (-not (Test-Path -LiteralPath (Join-Path $taskInstall 'parent-ready.txt'))) { throw 'Fixture parent did not start.' }
$taskPlan = @{ installation=$taskInstall; payload=$taskPayload; executable='Nonet.exe'; dataRoot=$taskData; backupRoot=(Join-Path $taskData 'Backups'); parentId=$taskParent.Id; parentStartedUtc=$taskParent.StartTime.ToUniversalTime(); manifest=@{schema=1;version='0.3.0-beta.99';rid='win-x64';files=$taskFiles} }
$taskPlanPath = Join-Path $taskStage 'apply-plan.json'
[IO.File]::WriteAllText($taskPlanPath, ($taskPlan | ConvertTo-Json -Depth 8))
$taskUpdater = Start-Process -FilePath (Join-Path $taskHelper 'Nonet.exe') -ArgumentList @('--apply-update', ('"'+$taskPlanPath+'"')) -WindowStyle Hidden -PassThru
Start-Sleep -Milliseconds 400
if ((Test-Path -LiteralPath (Join-Path $taskStage 'result.txt')) -or (Get-Content -LiteralPath (Join-Path $taskInstall 'notes.txt') -Raw) -ne 'obsolete application notes') { throw 'Helper did not wait for the old process.' }
[IO.File]::WriteAllText((Join-Path $taskInstall 'parent-exit.txt'), 'exit')
if (-not $taskUpdater.WaitForExit(20000) -or $taskUpdater.ExitCode -ne 0) { throw 'Update helper failed or timed out.' }
$taskDeadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
while (-not (Test-Path -LiteralPath (Join-Path $taskInstall 'restarted.txt')) -and [DateTimeOffset]::UtcNow -lt $taskDeadline) { Start-Sleep -Milliseconds 50 }
if (-not (Test-Path -LiteralPath (Join-Path $taskInstall 'restarted.txt')) -or (Get-Content -LiteralPath (Join-Path $taskData 'keep.txt') -Raw) -ne 'original user data' -or (Get-Content -LiteralPath (Join-Path $taskStage 'result.txt') -Raw) -ne 'success' -or (Get-Content -LiteralPath (Join-Path $taskInstall 'notes.txt') -Raw) -ne 'updated application notes') { throw 'Restart or data preservation failed.' }
Write-Host "PASS published update helper: waits for PID exit, replaces files, restarts and preserves Data. $taskRoot"
