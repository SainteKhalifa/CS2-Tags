# CS2-Tags (fork SainteKhalifa)

Plugin [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp/) qui ajoute des tags aux joueurs : préfixe et couleurs dans le chat, tag devant le pseudo au scoreboard et dans le killfeed. Les tags s'attribuent par SteamID64, par groupe ou par permission.

Fork de [daffyyyy/CS2-Tags](https://github.com/daffyyyy/CS2-Tags), adapté à la mise à jour CS2 du 22 septembre 2026 (« Rush Hour »). Cette mise à jour a réactivé les tags de clan, qui s'affichent désormais avec leur propre police.

## Différences avec le plugin original
- Le tag `scoreboard` est placé **devant le pseudo** (ex. `✦ADMIN✦ Pseudo`) au lieu du tag de clan : il s'affiche dans la police normale, au scoreboard comme dans le killfeed.
- Les **tags de clan de tous les joueurs sont masqués**, bots compris, y compris les tags de groupe Steam. Le plugin les vérifie toutes les 2 secondes, car le jeu peut les remettre quand les données Steam d'un joueur arrivent.
- Les **bots** ont leur propre entrée `bot`, facultative. Sans elle, ils n'ont pas de tag : ils ne prennent jamais le tag `everyone`. Le bot CSTV n'est jamais touché.
- Dans le chat, le plugin affiche le vrai pseudo, pour ne pas écrire le tag deux fois.
- `css_tags_reload` réapplique les tags immédiatement.
- Si le plugin est déchargé (`css_plugins unload`), les joueurs retrouvent leur vrai pseudo.
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
      "scoreboard": "✦ADMIN✦"        // Tag devant le pseudo (un espace est ajouté automatiquement)
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
      "scoreboard": ""               // Vide : pas de tag
    },
    "bot": {                         // Bots (facultatif)
      "scoreboard": "BOT"
    }
  }
}
```

- **Priorité** : l'entrée SteamID64 d'abord, puis les groupes (`#`) et permissions (`@`) dans l'ordre du fichier, puis `everyone`. Pour le tag du pseudo, une entrée dont `scoreboard` est vide est ignorée au profit de la suivante qui correspond.
- **Après une modification**, lancer `css_tags_reload` : c'est pris en compte tout de suite, sans redémarrer.
- **Longueur** : le pseudo complet (tag + pseudo) est limité à 127 octets ; au-delà, il est tronqué.

### Choisir ses symboles
Ces symboles existent dans les polices fournies avec CS2, ils s'affichent donc chez tous les joueurs (Windows, Linux, Steam Deck) :

`♛ ♚ ◆ ◇ ♦ ❖ ► ◄ ▸ ◂ ➤ « » ‹ › • ● · | ‖ ⚜ ⚔ ✔ ♠ ♣ ♥ ★ ✦ ✪ 【 】` ainsi que les petites capitales (`ᴀᴅᴍɪɴ`, `ᴠɪᴘ`).

À éviter :
- `[ ]` : CS2 affiche les tags de clan sous la forme `[TAG]`, la confusion est immédiate ;
- `( )` : le jeu affiche « (Pseudo) » quand un joueur prend le contrôle d'un bot ;
- les emojis (👑, 💎…) : ils ne sont pas dans les polices du jeu, d'où un risque de carrés vides ;
- `☠` : le plugin l'affiche déjà devant les messages des joueurs morts.

## Chat
- Les messages des joueurs tagués sont réécrits avec le préfixe et les couleurs de leur entrée ; `☠` est ajouté devant les messages des joueurs morts.
- Chat d'équipe : les messages sont précédés de `(T)` ou `(CT)`.
- Les messages qui commencent par `!`, `@`, `/` ou `.` (commandes), ainsi que le message `rtv`, ne sont pas réécrits.
- **Chat admin** : un message d'équipe qui commence par `@`, envoyé par un joueur ayant la permission `@css/chat`, est transmis à tous les joueurs ayant cette permission, sous la forme `(ADMIN) Pseudo: message`.

## Commandes (console serveur / RCON)
- `css_tags_reload` : recharge `tags.json` et réapplique les tags
- `css_tag_mute <SteamID64>` / `css_tag_unmute <SteamID64>` : bloque / débloque les messages d'un joueur dans le chat (le blocage s'arrête à sa déconnexion ou au changement de map)
- `css_tags_debug` : affiche pour chaque joueur et bot le pseudo appliqué, le tag de clan, l'id de clan et le dernier tag de clan effacé par le plugin

## Couleurs
Utilisables dans `prefix`, `nick_color` et `message_color`, entre accolades, sans tenir compte des majuscules (ex. `{LightRed}` ou `{LIGHTRED}`) :

`Default` `White` `DarkRed` `Green` `LightYellow` `LightBlue` `Olive` `Lime` `Red` `LightPurple` `Purple` `Grey` `Yellow` `Gold` `Silver` `Blue` `DarkBlue` `BlueGrey` `Magenta` `LightRed` `Orange`

`{TEAMCOLOR}` (en majuscules) : couleur de l'équipe du joueur (or pour les T, bleu pour les CT).

Les couleurs ne s'appliquent qu'au chat : au scoreboard et dans le killfeed, le pseudo s'affiche sans couleur.

## À savoir
- Le tag fait partie du pseudo : les autres plugins (classements, logs, notifications Discord…) voient aussi le pseudo avec le tag.
- La commande `status` du serveur affiche toujours le nom d'origine ; utiliser `css_tags_debug` pour voir le pseudo appliqué.

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
