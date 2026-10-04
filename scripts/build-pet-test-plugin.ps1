param([string]$Output = "artifacts/plugins/pet-controls-test.impp")
$ErrorActionPreference = "Stop"
$source = Join-Path $PSScriptRoot "../samples/MusicPet.Ui"
& (Join-Path $PSScriptRoot "pack-plugin.ps1") -Source $source -Output $Output
