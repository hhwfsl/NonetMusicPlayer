param([string]$Foundation = (Join-Path (Split-Path -Parent $PSScriptRoot) '../NonetMusicPlayerPlugin'), [switch]$Update)
$ErrorActionPreference = 'Stop'
$taskPlayer = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskFoundation = [IO.Path]::GetFullPath($Foundation)
if (-not (Test-Path -LiteralPath $taskFoundation) -and (Test-Path -LiteralPath (Join-Path $taskPlayer 'plugin-template/NonetMusicPlayerPlugin'))) { $taskFoundation = Join-Path $taskPlayer 'plugin-template/NonetMusicPlayerPlugin' }
if ($taskFoundation -eq $taskPlayer -or -not (Test-Path -LiteralPath (Join-Path $taskFoundation 'NonetMusicPlayerPlugin.slnx'))) { throw '目标必须是独立的插件基础工程。' }
$taskSource = Join-Path $taskPlayer 'src/NonetMusicPlayer.PluginSdk'
$taskTarget = Join-Path $taskFoundation 'sdk/NonetMusicPlayer.PluginSdk'
if (-not $taskTarget.StartsWith($taskFoundation.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'SDK 目标越界。' }
$taskFiles = Get-ChildItem -LiteralPath $taskSource -File | Where-Object { $_.Extension -in @('.cs','.csproj','.md') } | Sort-Object Name
$taskVersion = ([xml](Get-Content -LiteralPath (Join-Path $taskSource 'NonetMusicPlayer.PluginSdk.csproj') -Raw)).Project.PropertyGroup.Version
$taskHostVersion = ([xml](Get-Content -LiteralPath (Join-Path $taskPlayer 'src/NonetMusicPlayer.Desktop/NonetMusicPlayer.Desktop.csproj') -Raw)).Project.PropertyGroup.Version
$taskDigests = [ordered]@{}
function Get-TaskTextDigest([string]$Path) {
    # Git 的平台换行转换不代表 SDK 变更；统一 UTF-8 / LF 后再比较源码摘要。
    $taskContent = [IO.File]::ReadAllText($Path).Replace("`r`n", "`n")
    $taskSha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($taskSha.ComputeHash([Text.Encoding]::UTF8.GetBytes($taskContent))).Replace('-', '').ToLowerInvariant() }
    finally { $taskSha.Dispose() }
}
if($Update) { [IO.Directory]::CreateDirectory($taskTarget) | Out-Null }
foreach ($taskFile in $taskFiles) {
    $taskDestination = Join-Path $taskTarget $taskFile.Name
    $taskDigest = Get-TaskTextDigest $taskFile.FullName
    $taskDigests[$taskFile.Name] = $taskDigest
    if($Update) { Copy-Item -LiteralPath $taskFile.FullName -Destination $taskDestination -Force }
    if(-not (Test-Path -LiteralPath $taskDestination) -or (Get-TaskTextDigest $taskDestination) -ne $taskDigest) { throw "SDK 不同步：$($taskFile.Name)。使用 -Update 更新开发工程。" }
}
$taskDocument = Join-Path $taskPlayer 'docs/PLUGIN_DEVELOPMENT.md'
$taskReference = Join-Path $taskFoundation 'docs/PLUGIN_DEVELOPMENT.md'
if($Update) { Copy-Item -LiteralPath $taskDocument -Destination $taskReference -Force }
if(-not (Test-Path -LiteralPath $taskReference) -or (Get-TaskTextDigest $taskDocument) -ne (Get-TaskTextDigest $taskReference)) { throw '插件参考文档不同步。' }
$taskMetadata = Join-Path $taskFoundation 'sdk/SDK_VERSION.json'
if($Update) {
    # 生成的同步清单不包含绝对路径或账户信息，独立克隆后仍可构建。
    $taskJson = [ordered]@{sdkVersion=$taskVersion; playerVersion=$taskHostVersion; contractVersion=1; pageSchemaVersion=1; hashEncoding='utf8-lf'; files=$taskDigests} | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText($taskMetadata, $taskJson + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}
$taskRecorded = Get-Content -LiteralPath $taskMetadata -Raw | ConvertFrom-Json
if($taskRecorded.sdkVersion -ne $taskVersion -or $taskRecorded.playerVersion -ne $taskHostVersion -or $taskRecorded.hashEncoding -ne 'utf8-lf') { throw 'SDK 版本元数据不同步。' }
foreach($taskFile in $taskFiles) { if($taskRecorded.files.($taskFile.Name) -ne $taskDigests[$taskFile.Name]) { throw "SDK 摘要不同步：$($taskFile.Name)" } }
Write-Host "PASS 插件基础工程同步：SDK $taskVersion / Player $taskHostVersion"
