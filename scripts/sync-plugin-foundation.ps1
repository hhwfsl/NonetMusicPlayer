param([string]$Foundation = (Join-Path (Split-Path -Parent $PSScriptRoot) '../NonetMusicPlayerPlugin'), [switch]$Update)
$ErrorActionPreference = 'Stop'
$taskPlayer = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskFoundation = [IO.Path]::GetFullPath($Foundation)
if (-not (Test-Path -LiteralPath (Join-Path $taskFoundation 'NonetMusicPlayerPlugin.slnx'))) { throw '目标必须是独立的插件模板工程。' }
$taskProps = Get-Content -Raw -LiteralPath (Join-Path $taskFoundation 'Directory.Build.props')
if ($taskProps -notmatch 'NonetPlayerRoot' -or $taskProps -notmatch 'src/NonetMusicPlayer.PluginSdk' -or (Test-Path -LiteralPath (Join-Path $taskFoundation 'sdk'))) { throw '模板必须直接引用主项目 SDK，不能保留 vendored SDK。' }
# 仅同步接口文档，SDK 和打包器始终从当前主项目引用，不再维护代码快照。
foreach ($taskName in @('PLUGIN_DEVELOPMENT.md','UNIVERSAL_EXTENSIONS.md')) {
    $taskDocument = Join-Path $taskPlayer ('docs/' + $taskName)
    $taskReference = Join-Path $taskFoundation ('docs/' + $taskName)
    if ($Update) { Copy-Item -LiteralPath $taskDocument -Destination $taskReference -Force }
    if ([IO.File]::ReadAllText($taskDocument).Replace("`r`n", "`n") -ne [IO.File]::ReadAllText($taskReference).Replace("`r`n", "`n")) { throw '插件参考文档不同步，请使用 -Update。' }
}
Write-Host 'PASS 模板直接依赖宿主 SDK；插件接口文档一致。'
