# CS2-Tags (fork SainteKhalifa)

Plugin [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp/) qui ajoute des tags aux joueurs : préfixe et couleurs dans le chat, tag de clan au scoreboard et dans le killfeed. Les tags s'attribuent par SteamID64, par groupe ou par permission.

Fork de [daffyyyy/CS2-Tags](https://github.com/daffyyyy/CS2-Tags), adapté à la mise à jour CS2 du 22 septembre 2026 (« Rush Hour »). Cette mise à jour a réactivé les tags de clan : ils s'affichent à côté du pseudo, avec leur propre police, au scoreboard et dans le killfeed.

## Différences avec le plugin original
- Le tag `scoreboard` est affiché avec le **nouveau système de tags de clan de CS2**. Le pseudo du joueur n'est pas modifié.
- Le tag du plugin **remplace le tag du groupe Steam** du joueur. Le plugin le vérifie toutes les 2 secondes, car le jeu peut remettre le tag Steam quand les données Steam d'un joueur arrivent. Un joueur sans tag garde le tag de son groupe Steam.
- Les **bots sont laissés par défaut** : ils ne prennent jamais le tag `everyone`. L'étiquette « BOT » que le jeu affiche sur eux n'est pas un tag de clan : elle est ajoutée par le jeu lui-même et ne peut pas être retirée depuis le serveur. Le bot CSTV n'est jamais touché.
- `css_tags_reload` réapplique les tags immédiatement.
- Si le plugin est déchargé (`css_plugins unload`), les joueurs retrouvent le tag de leur groupe Steam.
- Nouvelle commande `css_tags_debug` pour vérifier ce qui est appliqué.
- Compilé pour CounterStrikeSharp 1.0.375 (.NET 10), avec une release automatique à chaque push.

## Installation
1. Télécharger `CS2-Tags.zip` depuis les [Releases](https://github.com/SainteKhalifa/CS2-Tags/releases).
2. Extraire le dossier `CS2-Tags` dans `game/csgo/addons/counterstrikesharp/plugins/`.
3. Au premier démarrage, un fichier `tags.json` d'exemple est créé dans ce dossier : l'adapter (voir ci-dessous), puis lancer `css_tags_reload`.

Le zip ne contient pas `tags.json` : votre configuration est conservée lors d'une mise à jour.

## Configuration
Fichier `addons/counterstrikesharp/plugins/CS2-Tags/tags.json`. Les commentaires ci-dessous sont là pour l'explication.
```jsonc
{
  "tags": {
    "#css/admin": {                  // Groupe CounterStrikeSharp
      "prefix": "{RED}✦ADMIN✦ ",     // Préfixe dans le chat
      "nick_color": "{RED}",         // Couleur du pseudo dans le chat
      "message_color": "{GREEN}",    // Couleur du message dans le chat
      "scoreboard": "✦ADMIN✦"        // Tag de clan (scoreboard et killfeed)
    },
    "@css/chat": {                   // Permission CounterStrikeSharp
      "prefix": "{GREEN}◆MODO◆ ",
      "nick_color": "{GREEN}",
      "message_color": "{GOLD}",
      "scoreboard": "◆MODO◆"
    },
    "76561198202892670": {           // SteamID64 (17 chiffres)
      "prefix": "{BLUE}♛VIP ",
      "nick_color": "{BLUE}",
      "message_color": "{BLUE}",
      "scoreboard": "♛VIP"
    },
    "everyone": {                    // Tous les autres joueurs (pas les bots)
      "team_chat": false,            // true : le préfixe s'applique aussi au chat général (sinon seulement au chat d'équipe)
      "prefix": "",
      "nick_color": "",
      "message_color": "",
      "scoreboard": ""               // Vide : pas de tag (le joueur garde le tag de son groupe Steam)
    }
  }
}
```

- **Priorité** : l'entrée SteamID64 d'abord, puis les groupes (`#`) et permissions (`@`) dans l'ordre du fichier, puis `everyone`. Pour le tag de clan, une entrée dont `scoreboard` est vide est ignorée au profit de la suivante qui correspond.
- **Après une modification**, lancer `css_tags_reload` : c'est pris en compte tout de suite, sans redémarrer.
- **Longueur** : le tag de clan est limité à 31 octets (un symbole comme `✦` en compte 3) ; au-delà, il est tronqué.
- **Serveur** : les tags de clan doivent être activés (`sv_clantags_enabled 1`, valeur par défaut). Avec `0`, le jeu masque tous les tags de clan, ceux du plugin compris.
- **Bots** : une entrée facultative `"bot": { "scoreboard": "..." }` donne un tag de clan aux bots. Par défaut il n'y en a pas, et c'est préférable : le jeu affiche déjà sa propre étiquette « BOT ».

### Choisir ses symboles
Ces symboles existent dans les polices fournies avec CS2, ils s'affichent donc chez tous les joueurs (Windows, Linux, Steam Deck) :

`♛ ♚ ◆ ◇ ♦ ❖ ► ◄ ▸ ◂ ➤ « » ‹ › • ● · | ‖ ⚜ ⚔ ✔ ♠ ♣ ♥ ★ ✦ ✪ 【 】` ainsi que les petites capitales (`ᴀᴅᴍɪɴ`, `ᴠɪᴘ`).

À éviter :
- `( )` : le jeu affiche « (Pseudo) » quand un joueur prend le contrôle d'un bot ;
- les emojis (👑, 💎…) : ils ne sont pas dans les polices du jeu, d'où un risque de carrés vides ;
- `☠` : le plugin l'affiche déjà devant les messages des joueurs morts.

## Chat
- Les messages des joueurs tagués sont réécrits avec le préfixe et les couleurs de leur entrée ; `☠` est ajouté devant les messages des joueurs morts.
- Chat d'équipe : les messages sont précédés de `(T)`, `(CT)` ou `(SPEC)`.
- Les messages qui commencent par `!`, `@`, `/` ou `.` (commandes), ainsi que le message `rtv`, ne sont pas réécrits.
- **Chat admin** : un message d'équipe qui commence par `@`, envoyé par un joueur ayant la permission `@css/chat`, est transmis à tous les joueurs ayant cette permission, sous la forme `(ADMIN) Pseudo: message`.

## Commandes (console serveur / RCON)
- `css_tags_reload` : recharge `tags.json` et réapplique les tags
- `css_tag_mute <SteamID64>` / `css_tag_unmute <SteamID64>` : bloque / débloque les messages d'un joueur dans le chat (le blocage s'arrête à sa déconnexion ou au changement de map)
- `css_tags_debug` : affiche la valeur de `sv_clantags_enabled`, puis pour chaque joueur et bot le pseudo, le tag de clan, l'id de clan et le tag de clan d'origine (groupe Steam) remplacé par le plugin

## Couleurs
Utilisables dans `prefix`, `nick_color` et `message_color`, entre accolades, sans tenir compte des majuscules (ex. `{LightRed}` ou `{LIGHTRED}`) :

`Default` `White` `DarkRed` `Green` `LightYellow` `LightBlue` `Olive` `Lime` `Red` `LightPurple` `Purple` `Grey` `Yellow` `Gold` `Silver` `Blue` `DarkBlue` `BlueGrey` `Magenta` `LightRed` `Orange`

`{TEAMCOLOR}` (en majuscules) : couleur de l'équipe du joueur, la même que dans le chat du jeu. Les `(T)` et `(CT)` du chat d'équipe utilisent aussi cette couleur.

Pour cela, le plugin envoie les messages comme le jeu lui-même, au nom du joueur. Si les couleurs d'équipe ne s'affichent pas correctement, ajouter `"game_team_colors": false` au début de `tags.json` (à côté de `"tags"`, pas dedans), puis lancer `css_tags_reload` : les messages sont alors envoyés comme avant, avec les couleurs les plus proches de la palette du chat (jaune clair pour les T, bleu clair pour les CT).

Les couleurs ne s'appliquent qu'au chat : au scoreboard et dans le killfeed, le tag de clan s'affiche avec la police et la couleur du jeu.

## À savoir
- Le pseudo n'est plus modifié : les autres plugins (classements, logs, notifications Discord…) voient le vrai pseudo.
- En passant d'une ancienne version (tag dans le pseudo) à celle-ci avec un rechargement à chaud, les pseudos déjà tagués le restent jusqu'à la reconnexion du joueur. Décharger l'ancienne version (`css_plugins unload CS2-Tags`) avant la mise à jour rend leur vrai pseudo aux joueurs.
- Les bots gardent l'étiquette « BOT » et l'icône de robot (colonne ping) affichées par le jeu : ce n'est pas un tag de clan, le serveur ne peut pas les retirer.
- La saisie du chat de CS2 est limitée à 127 caractères, et le jeu retire les guillemets doubles avant l'envoi : ce sont des limites du jeu, pas du plugin.

## Prérequis
[CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp/) **1.0.375** ou plus récent (.NET 10).

## Compilation
```
dotnet build CS2-Tags.csproj -c Release -o build
```
Sans SDK .NET installé, avec Docker :
```
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build CS2-Tags.csproj -c Release -o build
```
Copier ensuite le contenu de `build/`, sauf `CounterStrikeSharp.API.*`, dans `addons/counterstrikesharp/plugins/CS2-Tags/`.

## Versions et releases automatiques
Les versions suivent le format **v2.0.N**, où N est le numéro de compilation GitHub : il augmente tout seul à chaque compilation.

À chaque push sur `main` (ou via le bouton « Run workflow » de l'onglet Actions), GitHub Actions compile le plugin et publie une release « CS2-Tags v2.0.N » (tag `v2.0.N`) avec `CS2-Tags.zip`. Le même numéro s'affiche en jeu avec `css_plugins list`.

Pour une grosse mise à jour, changer `VersionBase` dans `CS2-Tags.csproj` (ex. `2.0` → `2.1`). Une compilation locale affiche `2.0.0`.

Sur un fork, les Actions doivent d'abord être activées dans l'onglet Actions.

## Crédits
Plugin original par [daffyy](https://github.com/daffyyyy) : [daffyyyy/CS2-Tags](https://github.com/daffyyyy/CS2-Tags). Pour soutenir l'auteur original :

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/Y8Y4THKXG)

Captures du plugin original :

![image](https://github.com/daffyyyy/CS2-Tags/assets/41084667/25dd3f2b-0604-41a2-b2bd-9be230db71e1)
![image](https://github.com/daffyyyy/CS2-Tags/assets/41084667/663a0de1-b875-48fc-bda5-56add5a4833b)
