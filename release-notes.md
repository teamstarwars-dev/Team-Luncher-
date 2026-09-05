## Performance v2 — Encore plus optimisé
- Filtre instances : debounce 200ms (pas de rebuild à chaque lettre)
- ServerPanel : truncation par TextLength au lieu de Lines[] (pas d'allocation tableau)
- Theme.Round : skip si taille identique (pas de Region recréée inutilement)
- GameLauncher : marquee 25ms → 60ms

## Garde de la v3.5.8
- SkinPreview : LockBits + cache couleur
- InstancesPage : images redimensionnées
- ModelViewer3D : Array.Sort
- ServerPanel : cache PID Java
- Détection .NET 8 au lancement
