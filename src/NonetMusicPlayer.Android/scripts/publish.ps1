param(
    [string]$SigningStore,
    [string]$Destination,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$androidProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Destination) { $Destination = [IO.Path]::GetFullPath((Join-Path $androidProject '../../publish/android')) }
$publishDirectory = [IO.Path]::GetFullPath($Destination)
if ($publishDirectory -eq [IO.Path]::GetPathRoot($publishDirectory) -or $publishDirectory -eq $androidProject) { throw 'Invalid publish directory' }
$buildText = Get-Content -Raw -LiteralPath (Join-Path $androidProject 'app/build.gradle.kts')
$version = [regex]::Match($buildText,'versionName = "([^"]+)"').Groups[1].Value
if (-not $version) { throw 'Version missing' }
if (-not $SkipBuild) {
    Push-Location $androidProject
    try {
        $arguments = @(':app:assembleRelease','--console=plain')
        if ($SigningStore) { $arguments += "-PnonetSigningStore=$SigningStore" }
        & (Join-Path $androidProject 'gradlew.bat') @arguments
        if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    } finally { Pop-Location }
}
$apk = Join-Path $androidProject 'app/build/outputs/apk/release/app-release.apk'
if (-not (Test-Path -LiteralPath $apk)) { throw 'Release APK missing' }
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
$latest = Join-Path $publishDirectory "Nonet-$version.apk"
Copy-Item -LiteralPath $apk -Destination $latest -Force
if ((Get-FileHash -LiteralPath $apk).Hash -ne (Get-FileHash -LiteralPath $latest).Hash) { throw 'Copy verification failed' }
# 仅删除已验证发布目录内的旧 APK，不删除用户数据或源文件。
Get-ChildItem -LiteralPath $publishDirectory -File -Filter '*.apk' | Where-Object { $_.FullName -ne $latest } | ForEach-Object {
    if ([IO.Path]::GetDirectoryName($_.FullName) -ne $publishDirectory.TrimEnd('\','/')) { throw 'Unexpected APK path' }
    Remove-Item -LiteralPath $_.FullName
}
Copy-Item -LiteralPath (Join-Path $androidProject 'README.md') -Destination (Join-Path $publishDirectory 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $androidProject 'CHANGELOG.md') -Destination (Join-Path $publishDirectory 'CHANGELOG.md') -Force
Copy-Item -LiteralPath (Join-Path $androidProject 'LICENSE') -Destination (Join-Path $publishDirectory 'LICENSE') -Force
Copy-Item -LiteralPath (Join-Path $androidProject 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $publishDirectory 'THIRD-PARTY-NOTICES.md') -Force
Copy-Item -LiteralPath (Join-Path $androidProject 'docs') -Destination $publishDirectory -Recurse -Force
Write-Output $latest
Get-FileHash -LiteralPath $latest -Algorithm SHA256
