param([string]$Configuration = 'Release', [string[]]$Platforms = @('windows','macos','linux'), [switch]$CleanOnly)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskProject = Join-Path $taskRoot 'src/NonetMusicPlayerCli/NonetMusicPlayerCli.csproj'
$taskVersion = ([xml](Get-Content -LiteralPath $taskProject -Raw)).Project.PropertyGroup.Version
$taskPublishRoot = Join-Path $taskRoot 'publish/cli'
$taskArtifacts = Join-Path $taskRoot 'artifacts'
$taskTimestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$env:NUGET_PACKAGES = Join-Path $taskRoot '.packages'
function Assert-TaskChild([string]$Candidate,[string]$Parent) {
    $taskResolved = [IO.Path]::GetFullPath($Candidate)
    if(-not $taskResolved.StartsWith([IO.Path]::GetFullPath($Parent).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw '发布路径越界。' }
    return $taskResolved
}
foreach($taskTarget in @(@{Name='windows';Rid='win-x64'},@{Name='macos';Rid='osx-x64'},@{Name='linux';Rid='linux-x64'}) | Where-Object { $_.Name -in $Platforms }) {
    $taskStage = Assert-TaskChild (Join-Path $taskArtifacts ('cli-stage-'+$taskTarget.Rid+'-'+$taskTimestamp)) $taskArtifacts
    $taskPackage = Join-Path $taskStage 'package'
    dotnet restore $taskProject --configfile (Join-Path $taskRoot 'NuGet.Config') -r $taskTarget.Rid --disable-parallel
    if($LASTEXITCODE -ne 0) { throw 'CLI 还原失败，原发布包未修改。' }
    dotnet publish $taskProject -c $Configuration -r $taskTarget.Rid --no-restore --self-contained true -o $taskPackage -p:PublishSingleFile=true -p:PublishTrimmed=true -p:TrimMode=partial -p:DebugType=None -p:DebugSymbols=false -p:EnableCompressionInSingleFile=true
    if($LASTEXITCODE -ne 0) { throw 'CLI 发布失败，原发布包未修改。' }
    # CLI 不引用 Avalonia，不需要图形库、字体、桌面文档或插件示例。
    foreach($taskExtra in Get-ChildItem -LiteralPath $taskPackage -File | Where-Object { $_.Name -match '\.(pdb|xml|deps\.json)$' }) { Remove-Item -LiteralPath $taskExtra.FullName -Force }
    $taskNotices = "NonetMusicPlayerCli $taskVersion`nSoundFlow / FFmpeg: https://github.com/LSXPrime/SoundFlow`nTagLibSharp: https://github.com/mono/taglib-sharp/tree/TaglibSharp-2.3.0.0`nTagLibSharp and FFmpeg remain external and replaceable; no modifications.`n"
    foreach($taskLicense in Get-ChildItem (Join-Path $taskRoot 'docs/licenses') -File) { if($taskLicense.Name -notin @('LGPL-2.1.txt', 'SoundFlow-notices.txt')) { continue }; $taskNotices += "`n"+$taskLicense.Name+"`n"+[IO.File]::ReadAllText($taskLicense.FullName) }
    $taskRuntime = Get-ChildItem (Join-Path $env:NUGET_PACKAGES ('microsoft.netcore.app.runtime.'+$taskTarget.Rid)) -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    foreach($taskName in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) { $taskLicense=Join-Path $taskRuntime.FullName $taskName; if(Test-Path -LiteralPath $taskLicense) { $taskNotices += "`n"+[IO.File]::ReadAllText($taskLicense) } }
    [IO.File]::WriteAllText((Join-Path $taskPackage 'THIRD_PARTY_NOTICES.txt'),$taskNotices,[Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/CLI.md') -Destination (Join-Path $taskPackage 'CLI.md')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/COMMANDS.md') -Destination (Join-Path $taskPackage 'COMMANDS.md')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'src/NonetMusicPlayerCli/README.md') -Destination (Join-Path $taskPackage 'README.md')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'src/NonetMusicPlayerCli/CHANGELOG.md') -Destination (Join-Path $taskPackage 'CHANGELOG.md')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE') -Destination (Join-Path $taskPackage 'LICENSE')
    if ($CleanOnly) {
        Write-Host "Clean CLI $($taskTarget.Name): $taskPackage"
        continue
    }
    $taskOutput = Assert-TaskChild (Join-Path $taskPublishRoot $taskTarget.Name) $taskPublishRoot
    [IO.Directory]::CreateDirectory($taskPublishRoot) | Out-Null
    if(Test-Path -LiteralPath $taskOutput) {
        $taskData=Join-Path $taskOutput 'Data'; if(Test-Path -LiteralPath $taskData) { Copy-Item -LiteralPath $taskData -Destination (Join-Path $taskPackage 'Data') -Recurse }
        $taskPrevious=Assert-TaskChild (Join-Path $taskArtifacts ('releases/previous-cli-'+$taskTimestamp+'/'+$taskTarget.Name)) $taskArtifacts
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($taskPrevious)) | Out-Null
        Move-Item -LiteralPath $taskOutput -Destination $taskPrevious
    }
    Move-Item -LiteralPath $taskPackage -Destination $taskOutput
    Remove-Item -LiteralPath $taskStage -Recurse -Force
    Write-Host "CLI $($taskTarget.Name): $taskOutput"
}
