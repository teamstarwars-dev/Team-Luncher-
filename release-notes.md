## v3.6.0 — Correction Discord + VPS caché + 10 corrections de bugs

### Discord Rich Presence
- Fix : la Rich Presence ne se réactivait pas toute seule après l'avoir désactivée
- Le toggle est maintenant respecté entre les sessions

### VPS / Pterodactyl
- Section VPS cachée dans les paramètres (réservée à l'admin)

### Corrections de bugs
- ServerHost : TunnelAddresses → ConcurrentDictionary (thread-safe)
- GameLauncher : JavaMajorCache → ConcurrentDictionary (thread-safe)
- GameLauncher : ProgressForm correctement disposée après usage
- InstancesPage : ContextMenuStrip correctement disposé après usage
- Theme : Title allouait un nouveau Font à chaque appel (fuite mémoire)
- InstancesPage : formula import CurseForge ne crash plus si fermé pendant l'async
- ServerPanel : code mort supprimé dans ShowPlayers

### Performance (depuis v3.5.9)
- SkinPreview : LockBits au lieu de GetPixel
- InstancesPage : images redimensionnées + debounce filtre
- ModelViewer3D : Array.Sort au lieu de LINQ
- ServerPanel : cache PID Java
- Theme.Round : skip si taille identique

### Deux versions disponibles
- **TeamLauncher.exe** (0.2 Mo) — nécessite .NET 8 Desktop Runtime
- **TeamLauncher-v3.6.0-sc.zip** (67 Mo) — tout inclus, rien à installer
