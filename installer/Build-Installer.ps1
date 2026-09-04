<#
.SYNOPSIS
    Publishes the win-x64 build, packages it with Inno Setup, and copies
    the result out - archiving whatever installer was there before it gets
    overwritten, and dropping the freshly built one into the shared Google
    Drive folder.

.DESCRIPTION
    Every run does, in order:
      1. LKG backup - any *.exe already sitting in installer\Output is
         copied into installer\Output\LKG (overwriting a same-named file
         there) BEFORE anything else touches Output. This is the last
         installer that was known to work, kept as a fallback if a new
         build turns out to be bad.
      2. `dotnet publish` the self-contained win-x64 build.
      3. `ISCC.exe` (Inno Setup 6) packages that into
         installer\Output\ChronoTravelersSetup-<version>.exe.
      4. The newly built installer is copied out to $DriveDir (the local
         folder Google Drive for desktop syncs to
         https://drive.google.com/drive/u/0/folders/1UHlTvdzEeIgQA9X1AxBTt3hnSphx0F18).
         Skipped with a warning if $DriveDir isn't set or doesn't exist -
         a missing/unmounted Drive sync shouldn't fail the build.

.PARAMETER Version
    Installer version - flows into both `dotnet publish -p:Version=` and
    Inno Setup's /DMyAppVersion=. Defaults to the version already baked
    into installer\ChronoTravelers.iss.

.EXAMPLE
    .\installer\Build-Installer.ps1
    .\installer\Build-Installer.ps1 -Version 0.2.0
#>
[CmdletBinding()]
param(
    [string]$Version = "0.1.1"
)

$ErrorActionPreference = "Stop"

# installer\Build-Installer.ps1 -> repo root is one level up from this script.
$RepoRoot    = Split-Path -Parent $PSScriptRoot
$InstallerDir = Join-Path $RepoRoot "installer"
$OutputDir   = Join-Path $InstallerDir "Output"
$LkgDir      = Join-Path $OutputDir "LKG"
$PublishDir  = Join-Path $RepoRoot "publish\win-x64"
$IssFile     = Join-Path $InstallerDir "ChronoTravelers.iss"

# TODO: set this to the local folder Google Drive for desktop syncs to
# https://drive.google.com/drive/u/0/folders/1UHlTvdzEeIgQA9X1AxBTt3hnSphx0F18
# e.g. "C:\Users\jdick\Google Drive\Installers" (Mirror mode) or
# "G:\My Drive\Installers" (Stream mode). Leave blank to skip that copy.
$DriveDir = ""

Write-Host "== ChronoTravelers installer build ($Version) ==" -ForegroundColor Cyan

# --- 1. Archive whatever installer is currently in Output, before it's overwritten ---
New-Item -ItemType Directory -Force -Path $LkgDir | Out-Null
$currentInstallers = Get-ChildItem -Path $OutputDir -Filter "*.exe" -File -ErrorAction SilentlyContinue
if ($currentInstallers) {
    foreach ($installer in $currentInstallers) {
        Write-Host "Archiving current installer to LKG: $($installer.Name)"
        Copy-Item -Path $installer.FullName -Destination $LkgDir -Force
    }
} else {
    Write-Host "No existing installer in Output - nothing to archive."
}

# --- 2. Publish the self-contained win-x64 build ---
Write-Host "Publishing win-x64 build..."
dotnet publish (Join-Path $RepoRoot "src\ChronoTravelers.Console\ChronoTravelers.Console.csproj") `
    -c Release -r win-x64 -o $PublishDir -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

# --- 3. Package with Inno Setup ---
$Iscc = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if (-not $Iscc) {
    foreach ($candidate in @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    )) {
        if (Test-Path $candidate) { $Iscc = $candidate; break }
    }
}
if (-not $Iscc) {
    throw "ISCC.exe (Inno Setup 6) not found. Install it (winget install JRSoftware.InnoSetup) or add it to PATH."
}

Write-Host "Building installer with Inno Setup ($Iscc)..."
& $Iscc "/DMyAppVersion=$Version" $IssFile
if ($LASTEXITCODE -ne 0) { throw "Inno Setup build failed (exit $LASTEXITCODE)." }

# --- 4. Copy the freshly built installer out to Google Drive ---
$newInstaller = Get-ChildItem -Path $OutputDir -Filter "*.exe" -File |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $newInstaller) {
    throw "Inno Setup reported success but no .exe was found in $OutputDir."
}

if ([string]::IsNullOrWhiteSpace($DriveDir)) {
    Write-Warning "`$DriveDir isn't set - skipping the copy to Google Drive. Edit this script to set it."
} elseif (-not (Test-Path $DriveDir)) {
    Write-Warning "Google Drive folder not found at '$DriveDir' (is Drive for desktop running/synced?) - skipping copy."
} else {
    Write-Host "Copying $($newInstaller.Name) to Google Drive ($DriveDir)..."
    Copy-Item -Path $newInstaller.FullName -Destination $DriveDir -Force
}

Write-Host "Done: $($newInstaller.FullName)" -ForegroundColor Green
