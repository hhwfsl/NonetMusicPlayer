param([string]$Output = "artifacts/plugins/snake-game-test.impp")
$ErrorActionPreference = "Stop"
$source = Join-Path $PSScriptRoot "../samples/SnakeGame.Ui"
& (Join-Path $PSScriptRoot "pack-plugin.ps1") -Source $source -Output $Output
