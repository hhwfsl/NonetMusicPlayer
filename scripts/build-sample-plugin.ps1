param([string]$Runtime = "win-x64", [string]$Output = "")
$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository "samples/NonetMusicPlayer.SampleProvider.Desktop/NonetMusicPlayer.SampleProvider.Desktop.csproj"
$stage = Join-Path $repository "artifacts/sample-provider-$Runtime"
$env:NUGET_PACKAGES = Join-Path $repository ".packages"
dotnet publish $project -c Release -r $Runtime --self-contained true -o $stage --configfile (Join-Path $repository "NuGet.Config") -p:PublishSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Sample provider build failed." }
$manifest = Get-Content -Raw (Join-Path $repository "samples/NonetMusicPlayer.SampleProvider.Desktop/manifest.json") | ConvertFrom-Json
$entry = if ($Runtime.StartsWith("win-")) { "SampleProvider.exe" } else { "SampleProvider" }
$manifest.entryPoints = @{ $Runtime = $entry }
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $stage "manifest.json") -Encoding utf8
Copy-Item -LiteralPath (Join-Path $repository "samples/NonetMusicPlayer.SampleProvider.Desktop/plugin_config_schema.json") -Destination $stage
if ([string]::IsNullOrWhiteSpace($Output)) { $Output = Join-Path $repository "artifacts/plugins/sample-provider-$Runtime.impp" }
& (Join-Path $PSScriptRoot "pack-plugin.ps1") -Source $stage -Output $Output
