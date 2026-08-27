# Publication de Team Launcher :
#  1. build autonome (self-contained, un seul dossier dans dist-autonome)
#  2. package Velopack (installateur + flux de mise à jour dans Releases\win)
#
# Usage : .\tools\publish.ps1 -Version 1.0.0
# Ensuite : envoie le contenu de Releases\win vers ton URL de flux
# (ex. GitHub Releases) et colle l'URL dans Paramètres → Mises à jour.
param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "==> Build autonome win-x64..." -ForegroundColor Cyan
dotnet publish "$root\src\TeamLauncher" -c Release -r win-x64 --self-contained true -o "$root\dist-autonome"
if ($LASTEXITCODE -ne 0) { throw "Échec du build." }

Write-Host "==> Package Velopack v$Version..." -ForegroundColor Cyan
dotnet vpk pack -u TeamLauncher -v $Version -p "$root\dist-autonome" `
    -e TeamLauncher.exe --packTitle "Team Launcher"
if ($LASTEXITCODE -ne 0) { throw "Échec du packaging Velopack (dotnet tool install -g vpk ?)." }

Write-Host ""
Write-Host "Terminé ! Fichiers dans $root\Releases\win :" -ForegroundColor Green
Get-ChildItem "$root\Releases\win" | Select-Object Name, Length
