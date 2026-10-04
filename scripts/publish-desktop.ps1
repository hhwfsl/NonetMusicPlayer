param([string]$Configuration = "Release", [string[]]$Platforms = @("windows", "macos", "linux"), [switch]$SkipInstaller, [switch]$CleanOnly)
$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src/NonetMusicPlayer.Desktop/NonetMusicPlayer.Desktop.csproj"
$desktopPublishRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "publish/desktop"))
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
$env:NUGET_PACKAGES = Join-Path $repositoryRoot ".packages"
$version = ([xml](Get-Content -Raw -LiteralPath $projectPath)).Project.PropertyGroup.Version
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$newline = [Environment]::NewLine
$targets = @(
    @{ Name = "windows"; Rid = "win-x64" },
    @{ Name = "macos"; Rid = "osx-x64" },
    @{ Name = "linux"; Rid = "linux-x64" }
) | Where-Object { $_.Name -in $Platforms }
function Assert-ChildPath([string]$Candidate, [string]$Parent) {
    $resolved = [IO.Path]::GetFullPath($Candidate)
    $prefix = [IO.Path]::GetFullPath($Parent).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe path: $resolved" }
    return $resolved
}
function Write-MacIcon([string]$Destination) {
    # Native images are generated from assets/icons/app-icon.svg, not a stale ICO export cache.
    $chunks = [Collections.Generic.List[byte[]]]::new()
    foreach ($entry in @(@{Size=256; Type='ic08'}, @{Size=512; Type='ic09'}, @{Size=1024; Type='ic10'})) {
        $pngPath = Join-Path $repositoryRoot "assets/icons/native/app-icon-$($entry.Size).png"
        if (-not (Test-Path -LiteralPath $pngPath)) { throw "Missing SVG-derived icon representation: $pngPath" }
        $png = [IO.File]::ReadAllBytes($pngPath)
        $chunkSize = [BitConverter]::GetBytes([int]($png.Length + 8)); [Array]::Reverse($chunkSize)
        $chunks.Add([byte[]]([Text.Encoding]::ASCII.GetBytes($entry.Type) + $chunkSize + $png))
    }
    $length = 8; foreach ($chunk in $chunks) { $length += $chunk.Length }
    $totalSize = [BitConverter]::GetBytes([int]$length); [Array]::Reverse($totalSize)
    $stream = [IO.MemoryStream]::new()
    try {
        $magic = [Text.Encoding]::ASCII.GetBytes("icns"); $stream.Write($magic, 0, $magic.Length); $stream.Write($totalSize, 0, $totalSize.Length)
        foreach ($chunk in $chunks) { $stream.Write($chunk, 0, $chunk.Length) }
        [IO.File]::WriteAllBytes($Destination, $stream.ToArray())
    } finally { $stream.Dispose() }
}
foreach ($target in $targets) {
    $stage = Assert-ChildPath (Join-Path $artifactRoot ("publish-stage-" + $target.Rid + "-" + $timestamp)) $artifactRoot
    $binary = Join-Path $stage "binary"
    dotnet restore $projectPath --configfile (Join-Path $repositoryRoot "NuGet.Config") -r $target.Rid --disable-parallel
    if ($LASTEXITCODE -ne 0) { throw "Restore failed for $($target.Rid)" }
    dotnet publish $projectPath -c $Configuration -r $target.Rid --no-restore --self-contained true -o $binary -p:PublishSingleFile=true -p:PublishTrimmed=true -p:TrimMode=partial -p:DebugType=None -p:DebugSymbols=false -p:EnableCompressionInSingleFile=true
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $($target.Rid); old package preserved." }
    Get-ChildItem -LiteralPath $binary -File | Where-Object { $_.Name -match '\.(pdb|xml|deps\.json|runtimeconfig\.dev\.json)$' } | Remove-Item -Force
    if ($target.Name -eq "windows") {
        # Program selects software-only drawing on Windows, so the ANGLE binary is unused.
        $unusedAngle = Assert-ChildPath (Join-Path $binary "av_libglesv2.dll") $stage
        if (Test-Path -LiteralPath $unusedAngle) { Remove-Item -LiteralPath $unusedAngle -Force }
    }
    $package = Join-Path $stage "package"
    [IO.Directory]::CreateDirectory($package) | Out-Null
    $runtime = Get-ChildItem -LiteralPath (Join-Path $env:NUGET_PACKAGES ("microsoft.netcore.app.runtime." + $target.Rid)) -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    $notices = @(
        "NonetMusicPlayer $version", "Third-party dependencies and source / license information", "",
        "Avalonia 12.1.1: https://github.com/AvaloniaUI/Avalonia",
        "CommunityToolkit.Mvvm 8.4.2: https://github.com/CommunityToolkit/dotnet",
        "SoundFlow 1.4.1 / FFmpeg adapter 1.4.0: https://github.com/LSXPrime/SoundFlow",
        "TagLibSharp 2.3.0: https://github.com/mono/taglib-sharp/tree/TaglibSharp-2.3.0.0",
        "Microsoft.Data.Sqlite 10.0.12: https://github.com/dotnet/efcore; SQLitePCLRaw 2.1.12: https://github.com/ericsink/SQLitePCL.raw; SQLite: https://www.sqlite.org/",
        "FFmpeg sources and build scripts: https://github.com/LSXPrime/SoundFlow/tree/c9bcf73512048181f25cb64cd824f4a406f0f96c",
        "TagLibSharp.dll and FFmpeg native libraries are deliberately external and replaceable. No modifications to these libraries are made by this project.", ""
    ) -join $newline
    foreach ($license in Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "docs/licenses") -File | Sort-Object Name) {
        $notices += $newline + "===== $($license.Name) =====" + $newline + (Get-Content -Raw -LiteralPath $license.FullName)
    }
    foreach ($name in @("LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT")) {
        $path = Join-Path $runtime.FullName $name
        if (Test-Path -LiteralPath $path) { $notices += $newline + "===== .NET $name =====" + $newline + (Get-Content -Raw -LiteralPath $path) }
    }
    if ($target.Name -eq "macos") {
        $bundle = Join-Path $package "Nonet.app/Contents"
        $macos = Join-Path $bundle "MacOS"; $resources = Join-Path $bundle "Resources"
        [IO.Directory]::CreateDirectory($macos) | Out-Null
        [IO.Directory]::CreateDirectory($resources) | Out-Null
        Get-ChildItem -LiteralPath $binary | Move-Item -Destination $macos
        Copy-Item -LiteralPath (Join-Path $repositoryRoot "scripts/macos/Info.plist") -Destination (Join-Path $bundle "Info.plist")
        Write-MacIcon (Join-Path $resources "app.icns")
        [IO.File]::WriteAllText((Join-Path $resources "THIRD_PARTY_NOTICES.txt"), $notices, [Text.UTF8Encoding]::new($false))
    } else {
        Get-ChildItem -LiteralPath $binary | Move-Item -Destination $package
        [IO.File]::WriteAllText((Join-Path $package "THIRD_PARTY_NOTICES.txt"), $notices, [Text.UTF8Encoding]::new($false))
    }
    # Offline manuals are opened by the native in-app viewer, without a browser dependency.
    $manualDestination = if ($target.Name -eq "macos") { $macos } else { $package }
    foreach ($language in @("zh-CN", "en-US", "ja-JP")) {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot "docs/UserManual.$language.md") -Destination $manualDestination
    }
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $manualDestination 'LICENSE')
    # 在复制用户数据之前构建 Release 更新资产；绝不将便携数据或引导配置打进更新包。
    if ($CleanOnly) {
        # 独立输出干净 ZIP；既不读取现有发布目录，也不复制或覆盖用户数据。
        & (Join-Path $PSScriptRoot 'package-desktop-update.ps1') -PackageDirectory $package -Rid $target.Rid -Version $version -OutputDirectory (Join-Path $desktopPublishRoot 'archives')
        Write-Host "Clean staging retained: $package"
        continue
    }
    & (Join-Path $PSScriptRoot 'package-desktop-update.ps1') -PackageDirectory $package -Rid $target.Rid -Version $version
    $output = Assert-ChildPath (Join-Path $desktopPublishRoot $target.Name) $desktopPublishRoot
    [IO.Directory]::CreateDirectory($desktopPublishRoot) | Out-Null
    if (Test-Path -LiteralPath $output) {
        # Preserve portable state; retain the complete previous package as a recoverable copy.
        $portableSource = if ($target.Name -eq "macos") { Join-Path $output "Nonet.app/Contents/MacOS" } else { $output }
        $portableDestination = if ($target.Name -eq "macos") { Join-Path $package "Nonet.app/Contents/MacOS" } else { $package }
        # 升级旧品牌便携版时也保留原引导配置，防止自定义数据目录失联。
        foreach ($dataName in @("Data", "Nonet.bootstrap.json", "LittleMusicPlayer.bootstrap.json")) {
            $portableData = Join-Path $portableSource $dataName
            if (Test-Path -LiteralPath $portableData) { Copy-Item -LiteralPath $portableData -Destination (Join-Path $portableDestination $dataName) -Recurse }
        }
        $bootstrap = @('Nonet.bootstrap.json','LittleMusicPlayer.bootstrap.json') | ForEach-Object { Join-Path $portableSource $_ } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if ($bootstrap) {
            try {
                $portableConfig = Get-Content -Raw -LiteralPath $bootstrap | ConvertFrom-Json
                foreach ($configuredData in @($portableConfig.dataDirectory, $portableConfig.backupDirectory)) {
                    if ([string]::IsNullOrWhiteSpace($configuredData)) { continue }
                    $configuredSource = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($configuredData)) { $configuredData } else { Join-Path $portableSource $configuredData }))
                    $outputPrefix = [IO.Path]::GetFullPath($output).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
                    if (-not $configuredSource.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $configuredSource)) { continue }
                    # The validated prefix allows a relative path on both Windows
                    # PowerShell 5.1 (.NET Framework) and PowerShell 7.
                    $configuredDestination = Assert-ChildPath (Join-Path $package $configuredSource.Substring($outputPrefix.Length)) $package
                    if (-not (Test-Path -LiteralPath $configuredDestination)) { [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($configuredDestination)) | Out-Null; Copy-Item -LiteralPath $configuredSource -Destination $configuredDestination -Recurse }
                }
            } catch { throw "Portable data preservation failed; previous package is unchanged: $($_.Exception.Message)" }
        }
        $backup = Assert-ChildPath (Join-Path $artifactRoot ("releases/previous-" + $timestamp + "/" + $target.Name)) $artifactRoot
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup)) | Out-Null
        Move-Item -LiteralPath $output -Destination $backup
    }
    Move-Item -LiteralPath $package -Destination $output
    Remove-Item -LiteralPath $stage -Recurse -Force
    $files = Get-ChildItem -LiteralPath $output -File -Recurse
    $size = ($files | Measure-Object Length -Sum).Sum
    Write-Host "$($target.Name): $($files.Count) required files, $([math]::Round($size / 1MB, 2)) MiB"
}
if ('windows' -in $Platforms -and -not $SkipInstaller -and -not $CleanOnly) { & (Join-Path $PSScriptRoot 'build-windows-installer.ps1') -Version $version }
Write-Host "Release $version available in $desktopPublishRoot"

