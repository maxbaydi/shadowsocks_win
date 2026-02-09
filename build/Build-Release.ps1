param(
    [switch]$SkipInstaller,
    [switch]$KeepPdb,
    [string]$SsLocalPath,
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$RepoRoot     = Split-Path $PSScriptRoot -Parent
$ProjectPath  = Join-Path $RepoRoot 'src\VibeShadowsocks.App\VibeShadowsocks.App.csproj'
$PublishDir   = Join-Path $RepoRoot 'dist\publish'
$OutputDir    = Join-Path $RepoRoot 'dist\VibeShadowsocks'
$ReleaseDir   = Join-Path $RepoRoot 'dist\releases'

$KeepLocales = @('en-us', 'en-US', 'ru', 'ru-RU')

if (-not $Version) {
    $propsFile = Join-Path $RepoRoot 'Directory.Build.props'
    [xml]$props = Get-Content $propsFile
    $Version = $props.Project.PropertyGroup.Version
    if (-not $Version) { $Version = '1.0.0' }
}

function Write-Step($msg) { Write-Host "`n>>> $msg" -ForegroundColor Cyan }

Write-Step 'Cleaning previous build'
if (Test-Path (Join-Path $RepoRoot 'dist')) {
    Remove-Item (Join-Path $RepoRoot 'dist') -Recurse -Force
}

Write-Step "Publishing self-contained (win-x64) v$Version"
dotnet publish $ProjectPath `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:WindowsAppSDKSelfContained=true `
    -p:Platform=x64 `
    -p:Version=$Version `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Write-Step 'Preparing distribution folder'
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
Copy-Item $PublishDir $OutputDir -Recurse

if (-not $KeepPdb) {
    Write-Step 'Removing PDB debug symbols'
    Get-ChildItem $OutputDir -Filter '*.pdb' -Recurse | Remove-Item -Force
}

Write-Step 'Removing unnecessary locale folders'
$localeDirs = Get-ChildItem $OutputDir -Directory | Where-Object {
    $_.Name -match '^[a-z]{2}(-[A-Za-z]{2,})?$' -or
    $_.Name -match '^[a-z]{2}-[A-Z][a-z]+-[A-Z]{2}$'
}
foreach ($dir in $localeDirs) {
    if ($KeepLocales -notcontains $dir.Name) {
        Remove-Item $dir.FullName -Recurse -Force
    }
}

Write-Step 'Creating tools\sslocal directory'
$ssLocalDir = Join-Path $OutputDir 'tools\sslocal'
New-Item $ssLocalDir -ItemType Directory -Force | Out-Null

if ($SsLocalPath -and (Test-Path $SsLocalPath)) {
    Copy-Item $SsLocalPath (Join-Path $ssLocalDir 'sslocal.exe')
    Write-Host "  Copied sslocal.exe from $SsLocalPath"
} else {
    @"
Place sslocal.exe here.
Download from: https://github.com/shadowsocks/shadowsocks-rust/releases
Choose the archive for Windows x86_64, extract sslocal.exe into this folder.
"@ | Set-Content (Join-Path $ssLocalDir 'README.txt')
    Write-Host '  sslocal.exe not provided — README.txt created'
}

$fileCount = (Get-ChildItem $OutputDir -Recurse -File).Count
$sizeMB    = [math]::Round((Get-ChildItem $OutputDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
Write-Step "Distribution ready: $fileCount files, $sizeMB MB"
Write-Host "  Path: $OutputDir"

if (-not $SkipInstaller) {
    Write-Step 'Installing/updating Velopack CLI (vpk)'
    dotnet tool update -g vpk

    New-Item $ReleaseDir -ItemType Directory -Force | Out-Null

    $icoPath = Join-Path $OutputDir 'app.ico'

    Write-Step "Packing Velopack release v$Version"
    vpk pack `
        --packId VibeShadowsocks `
        --packVersion $Version `
        --packDir $OutputDir `
        --mainExe VibeShadowsocks.App.exe `
        --icon $icoPath `
        --packTitle "VibeShadowsocks" `
        --outputDir $ReleaseDir

    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed with exit code $LASTEXITCODE" }

    Write-Host "  Velopack artifacts created in $ReleaseDir"
    Get-ChildItem $ReleaseDir | ForEach-Object {
        $fSizeMB = [math]::Round($_.Length / 1MB, 1)
        Write-Host "    $($_.Name) - ${fSizeMB} MB"
    }
} else {
    Write-Host "`n[!] Installer build skipped (use without -SkipInstaller to build)." -ForegroundColor Yellow
}

Write-Step 'Creating portable ZIP'
$zipDir = Join-Path $RepoRoot 'dist'
$zipPath = Join-Path $zipDir 'VibeShadowsocks-portable-win-x64.zip'
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$OutputDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
$zipItem = Get-Item $zipPath
$zipSizeMB = [math]::Round($zipItem.Length / 1MB, 1)
Write-Host "  ZIP: $zipPath - ${zipSizeMB} MB"

Write-Step "Build complete! (v$Version)"
Write-Host "  Portable ZIP : $zipPath"
if (Test-Path $ReleaseDir) {
    $setupExe = Get-ChildItem $ReleaseDir -Filter '*Setup*' | Select-Object -First 1
    if ($setupExe) {
        Write-Host "  Installer    : $($setupExe.FullName)"
    }
}
