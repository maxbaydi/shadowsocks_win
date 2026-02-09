param(
    [switch]$SkipInstaller,
    [switch]$KeepPdb,
    [string]$SsLocalPath
)

$ErrorActionPreference = 'Stop'

$RepoRoot     = Split-Path $PSScriptRoot -Parent
$ProjectPath  = Join-Path $RepoRoot 'src\VibeShadowsocks.App\VibeShadowsocks.App.csproj'
$PublishDir   = Join-Path $RepoRoot 'dist\publish'
$OutputDir    = Join-Path $RepoRoot 'dist\VibeShadowsocks'
$InstallerIss = Join-Path $PSScriptRoot 'installer.iss'
$InstallerOut = Join-Path $RepoRoot 'dist'

$KeepLocales = @('en-us', 'en-US', 'ru', 'ru-RU')

function Write-Step($msg) { Write-Host "`n>>> $msg" -ForegroundColor Cyan }

Write-Step 'Cleaning previous build'
if (Test-Path (Join-Path $RepoRoot 'dist')) {
    Remove-Item (Join-Path $RepoRoot 'dist') -Recurse -Force
}

Write-Step 'Publishing self-contained (win-x64)'
dotnet publish $ProjectPath `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:WindowsAppSDKSelfContained=true `
    -p:Platform=x64 `
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

$isccPath = $null
$candidates = @(
    'C:\InnoSetup6\ISCC.exe',
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
)
foreach ($c in $candidates) {
    if ($c -and (Test-Path $c)) { $isccPath = $c; break }
}
if (-not $isccPath) {
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { $isccPath = $cmd.Source }
}

if (-not $SkipInstaller -and $isccPath -and (Test-Path $InstallerIss)) {
    Write-Step 'Building installer with Inno Setup'
    & $isccPath $InstallerIss "/DDistDir=$OutputDir" "/DOutputDir=$InstallerOut"
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed' }
    Write-Host "  Installer created in $InstallerOut"
} elseif (-not $SkipInstaller) {
    Write-Host "`n[!] Inno Setup (ISCC.exe) not found - installer skipped." -ForegroundColor Yellow
    Write-Host '    Install: winget install JRSoftware.InnoSetup'
}

Write-Step 'Creating portable ZIP'
$zipPath = Join-Path $InstallerOut 'VibeShadowsocks-portable-win-x64.zip'
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$OutputDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
$zipItem = Get-Item $zipPath
$zipSizeMB = [math]::Round($zipItem.Length / 1MB, 1)
Write-Host "  ZIP: $zipPath - ${zipSizeMB} MB"

Write-Step 'Build complete!'
Write-Host "  Portable ZIP : $zipPath"
$installerExe = Join-Path $InstallerOut 'VibeShadowsocks-Setup.exe'
if (Test-Path $installerExe) {
    Write-Host "  Installer    : $installerExe"
}
