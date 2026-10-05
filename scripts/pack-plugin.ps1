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
$packager = Join-Path (Split-Path -Parent $PSScriptRoot) 'tools/NonetMusicPlayer.PluginPackager/NonetMusicPlayer.PluginPackager.csproj'
# Foundation 参数保留兼容旧脚本，但验证和打包只由当前宿主工程提供。
$env:NUGET_PACKAGES = Join-Path (Split-Path -Parent $PSScriptRoot) '.packages'
$arguments = @('run', '--project', $packager, '-c', 'Release', '--', 'pack', '--source', $sourcePath, '--output', $outputPath)
foreach ($file in $Include) { $arguments += @('--include', $file) }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw '插件打包或校验失败；未替换既有输出。' }
