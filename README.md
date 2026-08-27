# Team Launcher

Launcher Minecraft **natif Windows**, alternative à CurseForge avec une consommation RAM réduite au maximum.

## Objectif

| | CurseForge | Team Launcher (cible) |
|---|---|---|
| RAM au repos | ~300-500 Mo | **~30-50 Mo** |
| Techno UI | Electron (Chromium embarqué) | WinForms (contrôles natifs Windows) |
| Démarrage | Lent | < 1 s |
| Taille app | ~200 Mo | ~1 Mo (ou exe autonome ~70 Mo) |

## Stack technique

- **C# / .NET 8 — WinForms** : application 100% native, aucun Chromium ni WebView
- **Lancement du jeu** : processus Java séparé, le launcher peut se fermer pendant la partie
- **API mods** : Modrinth (gratuite et ouverte)

## Fonctionnalités prévues

- [ ] Installation / lancement de Minecraft (Vanilla, Forge, Fabric, NeoForge)
- [ ] Gestion d'instances multiples (isolées)
- [ ] Navigation / installation de mods via l'API Modrinth
- [ ] Gestion automatique des versions de Java
- [ ] Authentification Microsoft
- [ ] Mode "zéro RAM" : le launcher se ferme quand le jeu démarre

## Structure

```
Team Launcher/
├── src/
│   └── TeamLauncher/     # Projet C# WinForms (.NET 8)
├── ui/                   # (réservé - icônes, assets)
├── docs/                 # Documentation
└── README.md
```

## Build

```powershell
cd src\TeamLauncher
dotnet build -c Release
dotnet run -c Release
```

## Pourquoi si léger ?

1. **Contrôles natifs Windows** : pas de navigateur embarqué, l'UI est dessinée par le système
2. **Runtime compilé** : pas de machine virtuelle JS qui tourne en permanence
3. **Le launcher peut se fermer** une fois le jeu lancé (CurseForge reste ouvert en permanence)
