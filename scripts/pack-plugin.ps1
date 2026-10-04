param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Output,
    [string[]]$Include = @(),
    [string]$Foundation = (Join-Path (Split-Path -Parent $PSScriptRoot) '../NonetMusicPlayerPlugin')
)
$ErrorActionPreference = "Stop"
$sourcePath = [IO.Path]::GetFullPath($Source)
$outputPath = [IO.Path]::GetFullPath($Output)
if ([IO.Path]::GetExtension($outputPath) -ne ".impp") { throw "Plugin package output must use the .impp extension." }
if (-not (Test-Path -LiteralPath (Join-Path $sourcePath "manifest.json"))) { throw "manifest.json must be at the package root." }
if (Test-Path -LiteralPath $outputPath) { throw "Output exists; choose another path to avoid overwriting a plugin package." }
$foundationPath = [IO.Path]::GetFullPath($Foundation)
if (-not (Test-Path -LiteralPath $foundationPath)) { $foundationPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'plugin-template/NonetMusicPlayerPlugin' }
$packager = Join-Path $foundationPath 'tools/NonetMusicPlayerPlugin.Packager/NonetMusicPlayerPlugin.Packager.csproj'
if (-not (Test-Path -LiteralPath $packager)) { throw '需要同级 NonetMusicPlayerPlugin 基础工程，或通过 -Foundation 指定它的位置。' }
# 所有开发插件复用基础工程的打包器；避免原始 ZIP 路径绕过共享验证及最小文件选择。
& (Join-Path $PSScriptRoot 'sync-plugin-foundation.ps1') -Foundation $foundationPath
$env:NUGET_PACKAGES = Join-Path (Split-Path -Parent $PSScriptRoot) '.packages'
$arguments = @('run', '--project', $packager, '-c', 'Release', '--', 'pack', '--source', $sourcePath, '--output', $outputPath)
foreach ($file in $Include) { $arguments += @('--include', $file) }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw '插件打包或校验失败；未替换既有输出。' }
