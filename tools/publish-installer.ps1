# Publication de Team Launcher avec Inno Setup
# Build self-contained + package installer Windows
#
# Usage : .\tools\publish-installer.ps1 -Version 4.1.0
# Prérequis : Inno Setup 6.3+ installé (iscc.exe dans le PATH)
param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Team Launcher - Publication Installer" -ForegroundColor Cyan
Write-Host "  Version: $Version" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# 1. Build self-contained
Write-Host "==> [1/3] Build autonome win-x64..." -ForegroundColor Yellow
dotnet publish "$root\src\TeamLauncher" -c Release -r win-x64 --self-contained true -o "$root\dist"
if ($LASTEXITCODE -ne 0) { throw "Echec du build." }

$exePath = Join-Path $root "dist\TeamLauncher.exe"
if (-not (Test-Path $exePath)) {
    throw "TeamLauncher.exe introuvable dans dist/"
}
$size = (Get-Item $exePath).Length / 1MB
Write-Host "  Build OK ($([math]::Round($size, 1)) MB)" -ForegroundColor Green

# 2. Mettre a jour la version dans le script Inno Setup
Write-Host "==> [2/3] Mise a jour version dans installer.iss..." -ForegroundColor Yellow
$issPath = Join-Path $root "installer.iss"
$issContent = Get-Content $issPath -Raw
$issContent = $issContent -replace '#define MyAppVersion ".*"', "#define MyAppVersion `"$Version`""
Set-Content -Path $issPath -Value $issContent -NoNewline

# 3. Compiler l'installer avec Inno Setup
Write-Host "==> [3/3] Compilation Inno Setup..." -ForegroundColor Yellow

# Chercher iscc.exe
$iscc = $null
$candidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
foreach ($c in $candidates) {
    if (Test-Path $c) { $iscc = $c; break }
}
if (-not $iscc) {
    $iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
}
if (-not $iscc) {
    throw "Inno Setup introuvable. Installe-le depuis https://jrsoftware.org/isinfo.php"
}

Write-Host "  ISCC: $iscc" -ForegroundColor Gray
& $iscc $issPath
if ($LASTEXITCODE -ne 0) { throw "Echec de la compilation Inno Setup." }

# Verifier la sortie
$outputDir = Join-Path $root "installer-output"
if (Test-Path $outputDir) {
    $setupFiles = Get-ChildItem $outputDir -Filter "*.exe" | Sort-Object LastWriteTime -Descending
    if ($setupFiles.Count -gt 0) {
        $setup = $setupFiles[0]
        $setupMB = [math]::Round($setup.Length / 1MB, 1)
        Write-Host ""
        Write-Host "============================================" -ForegroundColor Green
        Write-Host "  Installer cree avec succes !" -ForegroundColor Green
        Write-Host "  Fichier: $($setup.Name)" -ForegroundColor Green
        Write-Host "  Taille:  $setupMB MB" -ForegroundColor Green
        Write-Host "  Dossier: $outputDir" -ForegroundColor Green
        Write-Host "============================================" -ForegroundColor Green
        Write-Host ""
        Write-Host "Prochaine etape : publier sur GitHub Releases" -ForegroundColor Cyan
        Write-Host "  .\tools\publish-github.ps1 -Version $Version" -ForegroundColor White
    }
} else {
    Write-Host "  Attention: dossier installer-output non trouve" -ForegroundColor Yellow
}
