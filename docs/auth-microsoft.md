# Connexion Microsoft officielle

Team Launcher n'utilise **aucun crack** : pour jouer en ligne, il faut se connecter avec un vrai
compte Microsoft possédant Minecraft Java.

**Aucune configuration n'est nécessaire**, ni pour toi ni pour les joueurs :
le launcher utilise un ID client intégré (comme Numek Launcher et les autres launchers tiers),
déjà accepté par Microsoft/Mojang.

Au moment de cliquer sur **Jouer** :
1. Le launcher génère un code et ouvre automatiquement ton navigateur sur la page de
   connexion Microsoft — le code y est déjà pré-rempli
2. Tu te connectes avec ton vrai compte Minecraft
3. Le launcher récupère la connexion tout seul

Le jeton est conservé localement (`%LocalAppData%\TeamLauncher\msauth.json`) :
les connexions suivantes sont automatiques et silencieuses.

> Note : le mode hors-ligne reste disponible (pseudo local), mais il ne permet que le solo,
> pas les serveurs officiels ni Essential en ligne.
