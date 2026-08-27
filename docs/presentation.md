# Team Launcher — Présentation

## Vue d'ensemble
- Launcher Minecraft Java natif Windows, alternative légère à CurseForge
- Développé en C# / .NET 8 avec WinForms (aucun Chromium ni WebView)
- Objectif : toutes les fonctionnalités essentielles avec une consommation minimale

## Performances
- RAM au repos : environ 18 à 50 Mo (contre 400 à 700 Mo pour CurseForge)
- Démarrage quasi instantané grâce au chargement paresseux des pages
- Mode "zéro RAM" : le launcher se met en veille dans la zone de notification pendant la partie et libère sa mémoire ; il revient à la fermeture du jeu
- Cache de vérification : après le premier téléchargement, les lancements suivants passent la préparation en moins d'une seconde (aucune revérification de hash, aucun appel réseau) ; le bouton Réparer force une revérification complète
- Exécutable autonome léger : dist\TeamLauncher.exe

### Comparatif

| | CurseForge | Team Launcher |
|---|---|---|
| RAM au repos | 400 à 700 Mo | 18 à 50 Mo |
| Technologie interface | Electron (Chromium embarqué) | WinForms (contrôles natifs Windows) |
| Pendant la partie | Launcher actif en permanence | Launcher en veille (zéro RAM) |
| Démarrage du launcher | Lent | Quasi instantané |
| Préparation avant de jouer (relance) | Vérification complète longue | Cache de vérification : moins d'une seconde |
| Taille de l'exécutable | Environ 200 Mo | Moins de 1 Mo |
| Loaders supportés | Forge, Fabric | Vanilla, Forge, Fabric, NeoForge |
| Prix | Gratuit avec publicités | Gratuit, sans publicité |

Note : le temps d'ouverture du jeu lui-même (machine virtuelle Java et chargement des mods)
dépend de Minecraft et des mods installés ; le launcher, lui, prépare tout en moins d'une
seconde dès que la version a déjà été téléchargée.

## Lancement du jeu
- Toutes les versions release officielles de Minecraft Java (fichiers téléchargés depuis les serveurs Mojang)
- Quatre loaders supportés :
    - Vanilla : lancement direct
    - Forge : installeur officiel exécuté en silence (même méthode que CurseForge)
    - Fabric : profils officiels via meta.fabricmc.net
    - NeoForge : installeur officiel depuis le Maven NeoForged
- Gestion automatique de Java : détection de la version requise selon la version du jeu, choix du Java le plus adapté installé sur la machine, téléchargement automatique si absent
- Authentification Microsoft officielle (OAuth device code) ou mode hors-ligne avec pseudo local — aucun crack
- Protection contre le double lancement : bouton Jouer verrouillé pendant la partie
- Barre de progression épurée avec pourcentage réel et étapes détaillées
- Journal technique complet (launcher.log + game-log.txt par instance)

## Gestion d'instances
- Création personnalisée : nom, description, image, loader, version exacte
- Import de dossiers existants (.minecraft) ou de modpacks .zip / .mrpack (compatible CurseForge)
- Export en archive zip partageable
- Duplication complète d'une instance pour tester sans risque
- Réparation : vérification des empreintes SHA-1 et retéléchargement des fichiers corrompus ou manquants
- Mise à jour automatique des mods via l'API Modrinth (détection par hash de fichier)
- Notes personnelles par instance, affichées au survol de la carte
- Recherche et filtre d'instances
- Mémoire allouée réglable globalement et par instance

## Les pages
- Accueil : message de bienvenue personnalisé, statistiques (instances, temps de jeu, lancements), cartes d'instances avec bouton Jouer, détail des temps au survol
- Tes instances : création, import, export, duplication, Essential, mise à jour des mods, réparation
- Explorateur : navigation dans les fichiers des instances sans quitter le launcher
- Skins : bibliothèque locale, aperçu 3D rotatif du modèle complet, application du skin en jeu même hors-ligne via CustomSkinLoader
- Exploration : recherche de mods, modpacks et shaders sur Modrinth avec loaders compatibles affichés, installation directe vers une instance
- Serveurs favoris : statut en temps réel (joueurs connectés, version, MOTD), double-clic pour rejoindre directement avec une instance choisie
- Édition : modification après coup de tout (nom, image, description, version, loader, mémoire, notes) et gestion des sauvegardes
- Compte : choix du mode Microsoft ou hors-ligne, modifiable à tout moment
- Paramètres : chemin Java, mémoire, dossier des instances, ID client Azure, couleurs personnalisables, compteur FPS optionnel, maintenance et journal

## Fonctionnalités supplémentaires
- Essential installé en un clic dans les instances compatibles (menu Social et cosmétiques en jeu)
- Sauvegardes automatiques des mondes à chaque fermeture du jeu (10 conservées, restauration en un clic depuis la page Édition)
- Raccourci bureau créé automatiquement au premier lancement
- Glisser-déposer : archive zip/mrpack sur la fenêtre = import direct ; image png/jpg sur une carte = changement d'image
- Double-clic sur une carte = lancer l'instance
- Alerte avant lancement si des mods semblent prévus pour une autre version que celle de l'instance
- Interface sombre entièrement personnalisable (fond, cartes, couleur d'accent)

## Confidentialité et légalité
- Aucun crack, aucun contournement : le mode Microsoft passe par l'authentification officielle OAuth
- Tous les fichiers du jeu proviennent exclusivement des serveurs officiels Mojang, Forge, Fabric, NeoForge et Modrinth
- Le jeton de connexion est stocké localement uniquement

## Build et distribution
- Compilation : dotnet build -c Release depuis src\TeamLauncher
- Publication : build\publish.bat génère dist\TeamLauncher.exe (exécutable unique, runtime .NET requis)
- Prérequis : .NET 8 Desktop Runtime (installé avec le SDK lors du développement)
