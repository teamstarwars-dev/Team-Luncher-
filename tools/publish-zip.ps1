# Génère le package Velopack (.nupkg) pour l'auto-update
# Ce package est uploadé sur GitHub Releases
param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$distDir = "$root\dist"
$outDir = "$root\releases"

Write-Host "==> Package Velopack v$Version..." -ForegroundColor Cyan

if (-not (Test-Path $distDir)) {
    throw "Dossier dist/ introuvable. Lance d'abord dotnet publish."
}

if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

# Créer le package Velopack (.nupkg)
Write-Host "  Création du package Velopack..." -ForegroundColor Yellow
dotnet vpk pack -u TeamLauncher -v $Version -p "$distDir" -e TeamLauncher.exe --packTitle "Team Launcher" --outputDir "$outDir"
if ($LASTEXITCODE -ne 0) { throw "Echec du packaging Velopack." }

# Aussi créer un zip pour les installs manuelles
$zipPath = "$outDir\TeamLauncher-update.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$distDir\*" -DestinationPath $zipPath -Force

$zipSize = (Get-Item $zipPath).Length / 1MB
Write-Host ""
Write-Host "Package Velopack créé dans $outDir" -ForegroundColor Green
Write-Host "Zip backup : TeamLauncher-update.zip ($([math]::Round($zipSize, 1)) MB)" -ForegroundColor Green
Write-Host ""
Write-Host "Prochaines étapes :" -ForegroundColor Yellow
Write-Host "  1. Uploade les .nupkg + zip sur GitHub Releases (tag v$Version)" -ForegroundColor White
Write-Host "  2. Velopack détecte automatiquement les mises à jour via GitHub Releases" -ForegroundColor White
