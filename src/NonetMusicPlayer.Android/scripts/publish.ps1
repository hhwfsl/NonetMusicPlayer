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
# 覆盖原包之前核对证书；工具链环境变量变化不能悄悄换掉发布签名。
$sdk = $env:ANDROID_HOME
if (-not $sdk) {
    $properties = Join-Path $androidProject 'local.properties'
    if (Test-Path -LiteralPath $properties) {
        $sdk = ((Get-Content -LiteralPath $properties | Where-Object { $_ -match '^sdk.dir=' } | Select-Object -First 1) -replace '^sdk.dir=','') -replace '\\:',':' -replace '\\\\','\'
    }
}
$signer = Get-ChildItem -LiteralPath (Join-Path $sdk 'build-tools') -Directory | Sort-Object Name -Descending | ForEach-Object {Join-Path $_.FullName 'apksigner.bat'} | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
if (-not $signer) {throw 'apksigner is required to verify upgrade compatibility'}
function Get-ApkSigner([string]$Path) {
    $output = & $signer verify --print-certs $Path 2>&1
    if ($LASTEXITCODE -ne 0) {throw "APK signature verification failed: $Path"}
    $match = [regex]::Match(($output -join "`n"),'certificate SHA-256 digest: ([0-9a-fA-F]{64})')
    if (-not $match.Success) {throw 'APK signer missing'}
    return $match.Groups[1].Value.ToLowerInvariant()
}
$digest = Get-ApkSigner $apk
$previous = Get-ChildItem -LiteralPath $publishDirectory -File -Filter 'Nonet-*.apk' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($previous) {
    if ((Get-ApkSigner $previous.FullName) -ne $digest) {throw 'Signing certificate differs from the previous APK. Supply the original -SigningStore; no files were replaced.'}
    $backupRoot = [IO.Path]::GetFullPath((Join-Path $androidProject '../../artifacts/android-release-backups'))
    $backupFolder = Join-Path $backupRoot ([DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $backupFolder -Force | Out-Null
    Copy-Item -LiteralPath $previous.FullName -Destination (Join-Path $backupFolder $previous.Name)
}
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
$checksum = (Get-FileHash -LiteralPath $latest -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($latest)
[IO.File]::WriteAllText((Join-Path $publishDirectory 'SHA256SUMS.txt'), $checksum + "`n", [Text.UTF8Encoding]::new($false))
