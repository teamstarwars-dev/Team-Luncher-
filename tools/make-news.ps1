# Ajoute une entrée d'actualité dans le flux JSON du launcher Team Launcher.
#
# Usage :
#   .\make-news.ps1 -Title "Version 1.3" -Text "Nouveau gestionnaire de mods." -Tag "MAJ"
#
# Le fichier généré est servi par le site local :
#   http://127.0.0.1:8080/news.json
# (à renseigner dans le launcher : Paramètres → URL des actualités)
param(
    [Parameter(Mandatory = $true)][string]$Title,
    [Parameter(Mandatory = $true)][string]$Text,
    [string]$Tag = "NOUVEAU",
    [int]$Keep = 30
)

$ErrorActionPreference = "Stop"
$newsPath = Join-Path $PSScriptRoot "..\site\maquette\news.json"

$items = @()
if (Test-Path $newsPath) {
    try {
        $parsed = ConvertFrom-Json -InputObject (Get-Content $newsPath -Raw -Encoding UTF8)
        # foreach : énumère proprement tableau OU objet unique
        foreach ($i in $parsed) { $items += $i }
    } catch { $items = @() }
}

$entry = [ordered]@{
    title = $Title
    date  = (Get-Date -Format "yyyy-MM-dd")
    tag   = $Tag
    text  = $Text
}

$all = @(@($entry) + $items)
if ($all.Count -gt $Keep) { $all = $all[0..($Keep - 1)] }

$json = ConvertTo-Json -InputObject @($all) -Depth 4
[System.IO.File]::WriteAllText($newsPath, $json, (New-Object System.Text.UTF8Encoding($false)))

Write-Host ("Actualite ajoutee -> {0} ({1})" -f $newsPath, $Tag)
