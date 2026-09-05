## Performance — Optimisé pour PC faibles
- SkinPreview : 69 000 GetPixel/sec → 0 (LockBits + cache couleur)
- SkinPreview : timer 33ms → 100ms (10 FPS au lieu de 30)
- InstancesPage : images redimensionnées à 210x110 au chargement
- InstancesPage : EnumerateFiles au lieu de GetFiles (pas d'allocation tableau)
- ModelViewer3D : Array.Sort au lieu de LINQ OrderByDescending
- ServerPanel : cache du PID Java (pas de scan toutes les 3s)

## Fix
- Crash "empty string" au démarrage corrigé
- Détection .NET 8 au lancement avec message clair
