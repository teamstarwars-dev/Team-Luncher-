# Génère le zip de mise à jour pour l'auto-update
# Ce zip contient tous les fichiers du dist/ à la racine
# Il est uploadé sur GitHub Releases et référencé dans version.json
param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$distDir = "$root\dist"
$outDir = "$root\releases"

Write-Host "==> Zip de mise à jour v$Version..." -ForegroundColor Cyan

if (-not (Test-Path $distDir)) {
    throw "Dossier dist/ introuvable. Lance d'abord dotnet publish."
}

if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

$zipPath = "$outDir\TeamLauncher-update.zip"

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# Zipper tout le dist/ (fichiers plats, pas de sous-dossiers)
Compress-Archive -Path "$distDir\*" -DestinationPath $zipPath -Force

$size = (Get-Item $zipPath).Length / 1MB
Write-Host ""
Write-Host "Zip créé : $zipPath ($([math]::Round($size, 1)) MB)" -ForegroundColor Green
Write-Host ""
Write-Host "Prochaines étapes :" -ForegroundColor Yellow
Write-Host "  1. Uploade ce zip sur GitHub Releases (tag v$Version)" -ForegroundColor White
Write-Host "  2. Met à jour version.json :" -ForegroundColor White
Write-Host "     `"url`": `"https://github.com/teamstarwars-dev/Team-Luncher-/releases/download/v$Version/TeamLauncher-update.zip`"" -ForegroundColor Gray
