<#
.SYNOPSIS
    Builds, publishes, packages, and optionally tags a new release of PSForge.

.DESCRIPTION
    Automates the PSForge release pipeline:
      1. Validates the supplied semantic version.
      2. Updates version strings in PSForge.csproj and installer.nsi.
      3. Publishes a framework-dependent Release build (win-x64).
      4. Compiles the NSIS installer to produce a distributable setup executable.
      5. Collects release notes.
      6. Commits the version bump, creates an annotated tag, and pushes.
      7. Cleans up old version tags (keeps only the latest).

    Modes
      Interactive  : prompts for version and notes (default).
      Parameterised: pass -Version and -Notes for CI/scripted use.
      Force        : -Force overwrites an existing tag.

    Prerequisites
      - .NET 8.0 SDK
      - NSIS 3.x (makensis on PATH, or installed at default location)

.PARAMETER Version
    Semantic version string (major.minor.patch[-prerelease]). Required.

.PARAMETER Notes
    One-line release notes. Prompted interactively if omitted.

.PARAMETER Force
    Overwrite an existing Git tag of the same version.

.PARAMETER SkipGit
    Build and publish only; skip all Git operations.

.PARAMETER SkipInstaller
    Publish only; skip the NSIS installer compilation step.

.PARAMETER DryRun
    Show what would happen without making changes.

.EXAMPLE
    .\update-application.ps1 -Version 1.2.0 -Notes "Added module search"

.EXAMPLE
    .\update-application.ps1            # interactive mode

.EXAMPLE
    .\update-application.ps1 -Version 1.0.1 -Notes "Hotfix" -SkipGit
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Position = 0)]
    [string]$Version,

    [Parameter(Position = 1)]
    [string]$Notes,

    [switch]$Force,
    [switch]$SkipGit,
    [switch]$SkipInstaller,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Colour helpers ──────────────────────────────────────────────────────────────
function Write-Step   { param([string]$Msg) Write-Host "▸ $Msg" -ForegroundColor Cyan }
function Write-Ok     { param([string]$Msg) Write-Host "  ✓ $Msg" -ForegroundColor Green }
function Write-Warn   { param([string]$Msg) Write-Host "  ⚠ $Msg" -ForegroundColor Yellow }
function Write-Fail   { param([string]$Msg) Write-Host "  ✗ $Msg" -ForegroundColor Red }

# ── Paths ───────────────────────────────────────────────────────────────────────
$ProjectDir   = $PSScriptRoot
$CsprojPath   = Join-Path $ProjectDir 'PSForge.csproj'
$NsiPath      = Join-Path $ProjectDir 'installer.nsi'
$PublishDir   = Join-Path $ProjectDir 'bin' 'publish'
$InstallerDir = Join-Path $ProjectDir 'bin' 'installer'

foreach ($requiredFile in @($CsprojPath, $NsiPath)) {
    if (-not (Test-Path $requiredFile)) {
        Write-Fail "Cannot find $requiredFile"
        exit 1
    }
}

# ── Locate makensis ─────────────────────────────────────────────────────────────
function Find-MakeNsis {
    # Check PATH first
    $cmd = Get-Command 'makensis' -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    # Check default install locations
    $defaultPaths = @(
        "${env:ProgramFiles(x86)}\NSIS\makensis.exe",
        "$env:ProgramFiles\NSIS\makensis.exe",
        "${env:LOCALAPPDATA}\NSIS\makensis.exe"
    )
    foreach ($p in $defaultPaths) {
        if (Test-Path $p) { return $p }
    }
    return $null
}

# ── 1. Version validation ──────────────────────────────────────────────────────
$SemverRegex = '^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<pre>[0-9A-Za-z\-\.]+))?$'

if (-not $Version) {
    $currentXml = [xml](Get-Content $CsprojPath -Raw)
    $currentVer = ($currentXml.Project.PropertyGroup | Select-Object -First 1).Version
    $Version = Read-Host "Enter new version (current: $currentVer)"
}

if ($Version -notmatch $SemverRegex) {
    Write-Fail "Invalid semver: '$Version'. Expected format: major.minor.patch[-prerelease]"
    exit 1
}

$csprojXml   = [xml](Get-Content $CsprojPath -Raw)
$currentVer  = ($csprojXml.Project.PropertyGroup | Select-Object -First 1).Version

if ($Version -eq $currentVer -and -not $Force) {
    Write-Fail "Version $Version is already current. Use -Force to overwrite."
    exit 1
}

Write-Step "Version: $currentVer → $Version"

# ── 2. Update manifests ────────────────────────────────────────────────────────
Write-Step "Updating version in PSForge.csproj and installer.nsi"

$assemblyVer = "$($Matches['major']).$($Matches['minor']).$($Matches['patch']).0"

if (-not $DryRun) {
    # Update csproj
    $csprojContent = Get-Content $CsprojPath -Raw
    $csprojContent = $csprojContent -replace '(<Version>)[^<]*(</Version>)',                "`${1}$Version`${2}"
    $csprojContent = $csprojContent -replace '(<AssemblyVersion>)[^<]*(</AssemblyVersion>)', "`${1}$assemblyVer`${2}"
    $csprojContent = $csprojContent -replace '(<FileVersion>)[^<]*(</FileVersion>)',         "`${1}$assemblyVer`${2}"
    Set-Content -Path $CsprojPath -Value $csprojContent -NoNewline

    # Update NSIS script version define
    $nsiContent = Get-Content $NsiPath -Raw
    $nsiContent = $nsiContent -replace '(!define\s+PRODUCT_VERSION\s+")[^"]*(")', "`${1}$Version`${2}"
    Set-Content -Path $NsiPath -Value $nsiContent -NoNewline

    Write-Ok "Versions updated in csproj and nsi"
} else {
    Write-Warn "[DRY RUN] Would update versions in csproj and nsi"
}

# ── 3. Publish (framework-dependent) ───────────────────────────────────────────
Write-Step "Publishing framework-dependent Release build (win-x64)…"

if (-not $DryRun) {
    # Clean previous publish output
    if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

    $publishArgs = @(
        'publish'
        $CsprojPath
        '-c', 'Release'
        '-r', 'win-x64'
        '--self-contained', 'false'
        '-o', $PublishDir
    )
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "dotnet publish failed (exit code $LASTEXITCODE). Rolling back version."
        & git checkout -- $CsprojPath $NsiPath 2>$null
        exit 1
    }

    $exe = Join-Path $PublishDir 'PSForge.exe'
    if (-not (Test-Path $exe)) {
        Write-Fail "Expected output not found: $exe"
        & git checkout -- $CsprojPath $NsiPath 2>$null
        exit 1
    }

    $fileCount = (Get-ChildItem $PublishDir -Recurse -File).Count
    $totalSize = [math]::Round(((Get-ChildItem $PublishDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB), 2)
    Write-Ok "Published $fileCount files ($totalSize MB) → $PublishDir"
} else {
    Write-Warn "[DRY RUN] Would run dotnet publish (framework-dependent)"
}

# ── 4. Build NSIS installer ────────────────────────────────────────────────────
if ($SkipInstaller) {
    Write-Warn "Skipping installer build (-SkipInstaller)"
} elseif ($DryRun) {
    Write-Warn "[DRY RUN] Would build NSIS installer"
} else {
    Write-Step "Building NSIS installer…"

    $makensis = Find-MakeNsis
    if (-not $makensis) {
        Write-Fail "makensis not found. Install NSIS 3.x (https://nsis.sourceforge.io) or add it to PATH."
        Write-Warn "Published files are available at: $PublishDir"
        Write-Warn "You can build the installer manually: makensis installer.nsi"
    } else {
        # Ensure output directory exists
        if (-not (Test-Path $InstallerDir)) {
            New-Item -ItemType Directory -Path $InstallerDir -Force | Out-Null
        }

        Push-Location $ProjectDir
        try {
            & $makensis $NsiPath
            if ($LASTEXITCODE -ne 0) {
                Write-Fail "makensis failed (exit code $LASTEXITCODE)"
                Pop-Location
                exit 1
            }
        } finally {
            Pop-Location
        }

        $setupExe = Join-Path $InstallerDir "PSForge-${Version}-Setup.exe"
        if (Test-Path $setupExe) {
            $setupSize = [math]::Round((Get-Item $setupExe).Length / 1MB, 2)
            Write-Ok "Installer → $setupExe ($setupSize MB)"
        } else {
            Write-Warn "Installer build completed but setup exe not found at expected path."
        }
    }
}

# ── 5. Release notes ───────────────────────────────────────────────────────────
if (-not $Notes) {
    $Notes = Read-Host "Enter release notes (single line)"
}
if ([string]::IsNullOrWhiteSpace($Notes)) {
    Write-Fail "Release notes must not be empty."
    exit 1
}
Write-Ok "Notes: $Notes"

# ── 6. Git operations ──────────────────────────────────────────────────────────
if ($SkipGit) {
    Write-Warn "Skipping Git operations (-SkipGit)"
} elseif ($DryRun) {
    Write-Warn "[DRY RUN] Would commit, tag v$Version, and push"
} else {
    Write-Step "Committing version bump"
    & git add $CsprojPath $NsiPath
    & git commit -m "chore: bump version to $Version"
    if ($LASTEXITCODE -ne 0) { Write-Fail "git commit failed"; exit 1 }

    $tagName = "v$Version"
    $existingTag = & git tag -l $tagName
    if ($existingTag -and -not $Force) {
        Write-Fail "Tag $tagName already exists. Use -Force to overwrite."
        exit 1
    }
    if ($existingTag -and $Force) {
        Write-Warn "Removing existing tag $tagName (forced)"
        & git tag -d $tagName 2>$null
        & git push origin --delete $tagName 2>$null
    }

    Write-Step "Creating annotated tag $tagName"
    & git tag -a $tagName -m "$Notes"
    if ($LASTEXITCODE -ne 0) { Write-Fail "git tag failed"; exit 1 }

    Write-Step "Pushing to origin"
    & git push origin HEAD
    & git push origin $tagName
    if ($LASTEXITCODE -ne 0) { Write-Fail "git push failed"; exit 1 }

    # ── 7. Cleanup old tags (keep only latest) ─────────────────────────────────
    Write-Step "Cleaning up old version tags"
    $allTags = & git tag -l 'v*' | Where-Object { $_ -ne $tagName }
    foreach ($old in $allTags) {
        Write-Warn "Removing old tag: $old"
        & git tag -d $old 2>$null
        & git push origin --delete $old 2>$null
    }

    Write-Ok "Release v$Version complete"
}

Write-Host ''
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
Write-Host "  PSForge v$Version" -ForegroundColor White
Write-Host "  Published files : $PublishDir" -ForegroundColor White
if (-not $SkipInstaller -and -not $DryRun) {
    Write-Host "  Installer       : $InstallerDir" -ForegroundColor White
}
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
