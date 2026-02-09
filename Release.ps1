$ErrorActionPreference = 'Stop'

function Write-Step($msg) { Write-Host "`n>>> $msg" -ForegroundColor Cyan }
function Write-Ok($msg) { Write-Host "  $msg" -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "  $msg" -ForegroundColor Yellow }

$RepoRoot = $PSScriptRoot
$PropsFile = Join-Path $RepoRoot 'Directory.Build.props'

[xml]$props = Get-Content $PropsFile
$currentVersion = $props.Project.PropertyGroup.Version
if (-not $currentVersion) { $currentVersion = '0.0.0' }

Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "   VibeShadowsocks Release Manager" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""
Write-Host "  Current version: " -NoNewline
Write-Host "v$currentVersion" -ForegroundColor Yellow
Write-Host ""

$parts = $currentVersion.Split('.')
$major = [int]$parts[0]
$minor = [int]$parts[1]
$patch = [int]$parts[2]

$suggestPatch = "$major.$minor.$($patch + 1)"
$suggestMinor = "$major.$($minor + 1).0"
$suggestMajor = "$($major + 1).0.0"

Write-Host "  Suggested versions:"
Write-Host "    [1] Patch : $suggestPatch  (bugfix)" -ForegroundColor Gray
Write-Host "    [2] Minor : $suggestMinor  (new feature)" -ForegroundColor Gray
Write-Host "    [3] Major : $suggestMajor  (breaking change)" -ForegroundColor Gray
Write-Host "    [4] Custom" -ForegroundColor Gray
Write-Host ""

$choice = Read-Host "  Select version type (1/2/3/4)"

$newVersion = switch ($choice) {
    '1' { $suggestPatch }
    '2' { $suggestMinor }
    '3' { $suggestMajor }
    '4' {
        $custom = Read-Host "  Enter version (e.g. 1.2.3)"
        if ($custom -notmatch '^\d+\.\d+\.\d+$') {
            Write-Host "  Invalid version format. Must be X.Y.Z" -ForegroundColor Red
            exit 1
        }
        $custom
    }
    default {
        Write-Host "  Invalid choice." -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "  New version: " -NoNewline
Write-Host "v$newVersion" -ForegroundColor Green
Write-Host ""

$confirm = Read-Host "  Proceed with v$newVersion release? (y/n)"
if ($confirm -ne 'y') {
    Write-Host "  Cancelled." -ForegroundColor Yellow
    exit 0
}

Write-Step "Updating version in Directory.Build.props"
$props.Project.PropertyGroup.Version = $newVersion
$props.Save($PropsFile)
Write-Ok "Version set to $newVersion"

Write-Step "Running build check"
Push-Location $RepoRoot
try {
    dotnet build -c Release
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
    Write-Ok "Build passed"
} finally {
    Pop-Location
}

Write-Step "Running tests"
Push-Location $RepoRoot
try {
    dotnet test -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "Tests failed" }
    Write-Ok "All tests passed"
} finally {
    Pop-Location
}

Write-Step "Committing version bump"
Push-Location $RepoRoot
try {
    git add Directory.Build.props
    git add -A
    git commit -m "release: v$newVersion"
    if ($LASTEXITCODE -ne 0) { throw "Git commit failed" }
    Write-Ok "Committed"
} finally {
    Pop-Location
}

Write-Step "Creating tag v$newVersion"
Push-Location $RepoRoot
try {
    git tag "v$newVersion"
    if ($LASTEXITCODE -ne 0) { throw "Git tag failed" }
    Write-Ok "Tag created"
} finally {
    Pop-Location
}

Write-Step "Pushing to origin"
Push-Location $RepoRoot
try {
    git push origin main
    if ($LASTEXITCODE -ne 0) { throw "Git push failed" }

    git push origin "v$newVersion"
    if ($LASTEXITCODE -ne 0) { throw "Tag push failed" }

    Write-Ok "Pushed to remote"
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "   Release v$newVersion initiated!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  GitHub Actions will now:" -ForegroundColor Gray
Write-Host "    1. Build the application" -ForegroundColor Gray
Write-Host "    2. Run tests" -ForegroundColor Gray
Write-Host "    3. Create Velopack installer + delta" -ForegroundColor Gray
Write-Host "    4. Publish GitHub Release with artifacts" -ForegroundColor Gray
Write-Host ""
Write-Host "  Monitor progress:" -ForegroundColor Gray
Write-Host "    https://github.com/maxbaydi/shadowsocks_win/actions" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Users with installed version will be notified" -ForegroundColor Gray
Write-Host "  about v$newVersion on their next app launch." -ForegroundColor Gray
Write-Host ""
