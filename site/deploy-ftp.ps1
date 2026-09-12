# deploy-ftp.ps1 — Deploie le site web via FTP
# Usage: .\deploy-ftp.ps1
# Les identifiants sont lus depuis ftp.env (gitignored)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$envFile   = Join-Path $scriptDir "ftp.env"
$siteDir   = Join-Path $scriptDir "website"

# --- Lecture de ftp.env ---
if (-not (Test-Path $envFile)) {
    Write-Host "ERREUR: $envFile introuvable." -ForegroundColor Red
    Write-Host "Cree un fichier ftp.env avec :" -ForegroundColor Yellow
    Write-Host '  FTP_HOST=ton-serveur.com' -ForegroundColor Yellow
    Write-Host '  FTP_USER=ton-user' -ForegroundColor Yellow
    Write-Host '  FTP_PASS=ton-mot-de-passe' -ForegroundColor Yellow
    Write-Host '  FTP_DIR=/htdocs' -ForegroundColor Yellow
    exit 1
}

$envContent = Get-Content $envFile | Where-Object { $_ -match '^\s*\w+\s*=' }
$envHash = @{}
foreach ($line in $envContent) {
    $key, $value = $line -split '=', 2
    $envHash[$key.Trim()] = $value.Trim()
}

$ftpHost = $envHash["FTP_HOST"]
$ftpUser = $envHash["FTP_USER"]
$ftpPass = $envHash["FTP_PASS"]
$ftpDir  = if ($envHash["FTP_DIR"]) { $envHash["FTP_DIR"] } else { "/htdocs" }

if (-not $ftpHost -or -not $ftpUser -or -not $ftpPass) {
    Write-Host "ERREUR: FTP_HOST, FTP_USER ou FTP_PASS manquant dans ftp.env" -ForegroundColor Red
    exit 1
}

# --- Vérification du dossier site ---
if (-not (Test-Path $siteDir)) {
    Write-Host "ERREUR: Dossier $siteDir introuvable." -ForegroundColor Red
    exit 1
}

Write-Host "=== Deploiement FTP ===" -ForegroundColor Cyan
Write-Host "Serveur:   $ftpHost" -ForegroundColor Gray
Write-Host "Utilisateur: $ftpUser" -ForegroundColor Gray
Write-Host "Distant:   $ftpDir" -ForegroundColor Gray
Write-Host "Local:     $siteDir" -ForegroundColor Gray
Write-Host ""

# --- Collecte des fichiers ---
$files = Get-ChildItem -Path $siteDir -Recurse -File | Where-Object {
    $_.FullName -notmatch '\\\.git\\' -and
    $_.FullName -notmatch '\\ftp\.env$' -and
    $_.FullName -notmatch '\\deploy-ftp\.ps1$' -and
    $_.FullName -notmatch '\\vercel\.json$'
}

$total = $files.Count
Write-Host "Fichiers a deployer: $total" -ForegroundColor Cyan
Write-Host ""

# --- Fonction: creer un dossier distant ---
function Ensure-FtpDirectory($ftpPath) {
    try {
        $listReq = [System.Net.FtpWebRequest]::Create("$ftpHost$ftpPath")
        $listReq.Method = [System.Net.WebRequestMethods+Ftp]::ListDirectoryDetails
        $listReq.Credentials = New-Object System.Net.NetworkCredential($ftpUser, $ftpPass)
        $listReq.UsePassive = $true
        $listReq.Timeout = 10000
        $null = $listReq.GetResponse()
        $listReq.GetResponse().Close()
    }
    catch {
        # Le dossier n'existe pas, on le crée
        try {
            $mkDirReq = [System.Net.FtpWebRequest]::Create("$ftpHost$ftpPath")
            $mkDirReq.Method = [System.Net.WebRequestMethods+Ftp]::MakeDirectory
            $mkDirReq.Credentials = New-Object System.Net.NetworkCredential($ftpUser, $ftpPass)
            $mkDirReq.UsePassive = $true
            $mkDirReq.Timeout = 10000
            $mkDirReq.GetResponse().Close()
            Write-Host "  + Dossier cree: $ftpPath" -ForegroundColor DarkGray
        }
        catch {
            Write-Host "  ! Impossible de creer: $ftpPath" -ForegroundColor DarkYellow
        }
    }
}

# --- Fonction: uploader un fichier ---
function Upload-FtpFile($localPath, $remotePath) {
    $fileSize = (Get-Item $localPath).Length
    $req = [System.Net.FtpWebRequest]::Create("$ftpHost$remotePath")
    $req.Method = [System.Net.WebRequestMethods+Ftp]::UploadFile
    $req.Credentials = New-Object System.Net.NetworkCredential($ftpUser, $ftpPass)
    $req.UseBinary = $true
    $req.UsePassive = $true
    $req.Timeout = 30000
    $req.ContentLength = $fileSize

    $fileStream = [System.IO.File]::OpenRead($localPath)
    $ftpStream = $req.GetRequestStream()
    $buffer = New-Object byte[] 8192
    $bytesRead = 0
    while (($bytesRead = $fileStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
        $ftpStream.Write($buffer, 0, $bytesRead)
    }
    $ftpStream.Close()
    $fileStream.Close()
    $req.GetResponse().Close()
}

# --- Déploiement ---
$count = 0
foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($siteDir.Length).Replace('\', '/')
    $remotePath = "$ftpDir$relativePath"
    $remoteDir = $remotePath.Substring(0, $remotePath.LastIndexOf('/'))

    # Créer les dossiers parents si besoin
    $parts = $remoteDir.Split('/') | Where-Object { $_ }
    $current = ""
    foreach ($part in $parts) {
        $current += "/$part"
        Ensure-FtpDirectory $current
    }

    # Uploader
    $count++
    $pct = [math]::Round(($count / $total) * 100)
    Write-Host "[$pct%] $relativePath" -ForegroundColor White
    try {
        Upload-FtpFile $file.FullName $remotePath
    }
    catch {
        Write-Host "  ERREUR: $_" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=== Deploiement termine: $count fichiers ===" -ForegroundColor Green
