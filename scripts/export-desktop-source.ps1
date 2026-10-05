param([string]$Destination, [string]$Foundation)
$ErrorActionPreference = 'Stop'
$taskSource = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if (-not $Destination) { $Destination = Join-Path $taskSource ('artifacts/github-source-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$taskDestination = [IO.Path]::GetFullPath($Destination)
$taskAllowed = [IO.Path]::GetFullPath((Join-Path $taskSource 'artifacts')).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
if (-not $taskDestination.StartsWith($taskAllowed, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $taskDestination)) { throw '导出目标必须是 artifacts 下尚不存在的独立目录。' }
# Foundation 参数只为兼容旧调用保留；本次公开源码不含模板或任何具体插件。
$taskIgnoredDirectories = @('.git','.vs','.idea','.vscode','.codex','.agents','bin','obj','artifacts','publish','dist','build','samples','.gradle','.kotlin','node_modules','.packages','.packages-feed','Data','Logs','Backups')
function Copy-TaskTree([string]$Source, [string]$Target) {
    [IO.Directory]::CreateDirectory($Target) | Out-Null
    foreach ($taskItem in Get-ChildItem -LiteralPath $Source -Force) {
        if ($taskItem.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "拒绝链接：$($taskItem.FullName)" }
        if ($taskItem.PSIsContainer) {
            if ($taskItem.Name -notin $taskIgnoredDirectories) { Copy-TaskTree $taskItem.FullName (Join-Path $Target $taskItem.Name) }
        } elseif ($taskItem.Name -ne 'NuGet.Offline.Config' -and $taskItem.Name -notmatch '\.(user|suo|log|cache|jks|keystore|impp)$' -and $taskItem.Name -ne 'local.properties') {
            Copy-Item -LiteralPath $taskItem.FullName -Destination (Join-Path $Target $taskItem.Name)
        }
    }
}
[IO.Directory]::CreateDirectory($taskDestination) | Out-Null
foreach ($taskFile in @('NonetMusicPlayer.slnx','Directory.Build.props','global.json','NuGet.Config','.editorconfig','.gitignore','.gitattributes','README.md','CHANGELOG.md','LICENSE','AGENTS.md')) {
    Copy-Item -LiteralPath (Join-Path $taskSource $taskFile) -Destination (Join-Path $taskDestination $taskFile)
}
foreach ($taskFolder in @('src/NonetMusicPlayer.Desktop','src/NonetMusicPlayer.Core','src/NonetMusicPlayerCli','src/NonetMusicPlayer.PluginSdk','tools/NonetMusicPlayer.PluginPackager','tests','assets/icons','scripts/macos','scripts/windows-installer')) {
    Copy-TaskTree (Join-Path $taskSource $taskFolder) (Join-Path $taskDestination $taskFolder)
}
foreach ($taskScript in @('publish-desktop.ps1','publish-cli.ps1','package-desktop-update.ps1','verify-clean-desktop.ps1','build-windows-installer.ps1','sync-plugin-foundation.ps1','pack-plugin.ps1','export-desktop-source.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $taskScript) -Destination (Join-Path $taskDestination 'scripts')
}
[IO.Directory]::CreateDirectory((Join-Path $taskDestination 'docs')) | Out-Null
foreach ($taskDocument in @('CLI.md','COMMANDS.md','LOCALIZATION.md','PLUGIN_DEVELOPMENT.md','RELEASE_VALIDATION.md','STORAGE.md','UI_LAYOUT.md','UPDATES.md','USER_MANUAL.md','UserManual.zh-CN.md','UserManual.en-US.md','UserManual.ja-JP.md','WINDOWS_INSTALLER.md')) {
    Copy-Item -LiteralPath (Join-Path $taskSource ('docs/' + $taskDocument)) -Destination (Join-Path $taskDestination 'docs')
}
Copy-TaskTree (Join-Path $taskSource 'docs/licenses') (Join-Path $taskDestination 'docs/licenses')
Copy-TaskTree (Join-Path $taskSource 'docs/layouts') (Join-Path $taskDestination 'docs/layouts')
# 仅复制当前源码，不读取主工程 .git，因此移动端及旧 Git 历史不会进入快照。
$taskFiles = Get-ChildItem -LiteralPath $taskDestination -File -Recurse
foreach ($taskFile in $taskFiles) {
    $taskRelative = [IO.Path]::GetRelativePath($taskDestination,$taskFile.FullName).Replace('\','/')
    if ($taskRelative -match '(^|/)(\.git|Data|Logs|Backups|bin|obj|publish|artifacts|samples|plugin-template|MyNonetMusicPlayerPlugin|NonetMusicPlayerPlugin)/|\.Android/|\.IOS/|\.(apk|aab|ipa|db|sqlite|impp|zip)$') { throw "禁止公开的文件：$taskRelative" }
    if ($taskFile.Extension -in @('.cs','.json','.xml','.config','.ps1','.md','.axaml')) {
        $taskText = [IO.File]::ReadAllText($taskFile.FullName)
        if ($taskText -match '(gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----|C:[\\/]+Users[\\/]+)') { throw "需要人工审查的敏感内容：$taskRelative" }
    }
}
Write-Host "Desktop-only source snapshot: $taskDestination ($($taskFiles.Count) files, no Git history)"
