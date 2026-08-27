# Stratégies transverses — API locale, Discord, Plugins, Multi-copilotes

## 12.1 API locale (Optimus Bridge)

Hébergée **dans le processus** de l'application, liée à `127.0.0.1` uniquement.
Sert : l'UI (à terme), le bot Discord, les plugins, un compagnon tablette/SIMPIT, et de futurs
clients. Elle n'est **jamais** exposée sur le réseau local.

### Ce qui est livré (2026-08-27)

Le tableau ci-dessous décrit la cible. Ce qui existe aujourd'hui en est le socle exécutable :

| Méthode | Route | Portée |
|---|---|---|
| `GET` | `/api/status` | `read` |
| `GET` | `/api/commands` | `read` |
| `POST` | `/api/intents/resolve` | `read` — résout un énoncé **sans rien exécuter** |
| `POST` | `/api/commands/{id}/execute` | `execute` |
| `POST` | `/api/utterance` | `execute` — même chemin que la voix |
| `POST` | `/api/say` | `write` |
| `POST` | `/api/system/killswitch` · `/api/system/simulation` | `write` |
| `WS` | `/ws/events` | `read` — trames `activity` et `state` |

Le reste du tableau cible — CRUD des copilotes, des commandes, des bindings, historique,
statistiques — reste à faire : l'écran couvre déjà ces gestes, et les ouvrir à l'API sans besoin
exprimé aurait été de la surface offerte pour rien.

**`HttpListener` plutôt que Kestrel.** Une poignée de routes sur la boucle locale ne justifie pas
d'embarquer ASP.NET Core dans une publication autonome déjà à 76 Mo. Et surtout : mesuré le
2026-08-27, `HttpListener` accepte `http://127.0.0.1:port/` **sans aucun privilège** mais refuse
`http://+:port/` — l'écoute sur toutes les interfaces — à qui n'est pas administrateur. Optimus
s'installant par utilisateur, sans UAC (D58), il lui est donc *impossible* de s'exposer au
réseau, même par erreur de programmation. La promesse §81-83 est portée par le système
d'exploitation, pas seulement par une ligne qu'un jour quelqu'un modifierait.

**Une exécution prend le temps que prend la parole.** `/execute` et `/utterance` ne rendent la
main qu'une fois la réplique du copilote prononcée — mesuré à environ 4 s pour un refus bavard.
C'est délibéré : l'API suit exactement le chemin de la voix, réponse comprise. Un client qui ne
veut pas attendre écoute `/ws/events` plutôt que la réponse HTTP.

### Routes

| Méthode | Route | Rôle | Protection |
|---|---|---|---|
| GET | `/api/status` | état complet (voix, jeu, copilote, latences, simulation) | token · lecture |
| GET | `/api/copilots` · `/api/copilots/{id}` | liste / détail | token · lecture |
| POST | `/api/copilots` · PUT/DELETE `/api/copilots/{id}` | CRUD | token · **écriture** |
| POST | `/api/copilots/{id}/activate` | changer de copilote actif | token · écriture |
| GET | `/api/commands` (`?q=&category=&favorite=`) | catalogue | token · lecture |
| POST/PUT/DELETE | `/api/commands[/{id}]` | CRUD des commandes utilisateur | token · **écriture** |
| POST | `/api/commands/{id}/test` | exécution **forcée en simulation** | token · écriture |
| POST | `/api/commands/{id}/execute` | exécution réelle | token · **exécution** + guard |
| POST | `/api/intents/resolve` | texte → intent (sans exécuter) | token · lecture |
| GET/PUT | `/api/bindings/{profile}` | profil de binding | token · écriture |
| POST | `/api/bindings/import` | import d'un XML SC | token · écriture |
| GET/PUT | `/api/profiles[/{id}]` | profils utilisateur | token · écriture |
| GET | `/api/history?limit=&since=` | historique | token · lecture |
| GET | `/api/analytics/*` | statistiques | token · lecture |
| POST | `/api/say` | faire parler le copilote | token · écriture |
| POST | `/api/system/killswitch` · `/api/system/simulation` | sécurité | token · **écriture** |
| WS | `/ws/events` | flux temps réel (état, commandes, traces) | token |

### Modèle de sécurité

1. **Token bearer** généré au premier lancement, 256 bits, stocké chiffré (DPAPI), affiché dans
   Settings, révocable/régénérable.
2. **Trois portées** : `read`, `write`, `execute`. Un client reçoit le minimum
   (le bot Discord démarre en `read` seul).
3. `execute` **repasse toujours par l'`ExecutionGuard`** : simulation, kill switch, focus jeu,
   permissions, `dangerous`. Aucune route ne court-circuite le point de contrôle unique.
4. **Rate limiting** par client (défaut : 30 exécutions/min) + journalisation de la source
   (`source = api | discord | plugin`) dans l'historique.
5. **CORS fermé** par défaut ; ouverture explicite d'une origine pour le compagnon tablette.
6. Écoute LAN possible **uniquement** via une option affichée avec un avertissement clair,
   token obligatoire, et jamais activée par défaut.

---

## 12.2 Discord (Optimus Link)

### Deux modes, un seul principe

```
MODE LOCAL (V1, par défaut)                MODE RELAIS (V2, optionnel)

 Discord ──► bot hébergé DANS               Discord ──► relais ──WS sortant──► Optimus
             Optimus (token perso)                       (n'a que des intents)
             │                                                        │
             └► ExecutionGuard ► clavier local                        └► ExecutionGuard ► clavier local
```

Dans **les deux cas** : le relais/Discord ne transmet **jamais** une touche, seulement un
`intent_id` + paramètres. La connexion est **sortante**. La machine cible valide tout localement.

Le mode local est recommandé par défaut : il ne nécessite aucune infrastructure, et rend
l'isolation (§81–83) vraie *par construction* et non par politique.

### Appairage

```
1. Optimus (Settings ▸ Discord) : [ Générer un code d'appairage ]  →  OPT-7K3F-92XA (10 min)
2. Discord : /optimus pair OPT-7K3F-92XA
3. Optimus vérifie le code, crée un DiscordLink :
   { discord_user_id, permissions: { view_status:true, view_commands:true,
                                     execute_commands:FALSE, modify_config:FALSE } }
4. Le propriétaire élève les permissions à la main, par utilisateur, dans l'UI.
5. Révocation en un clic ; expiration automatique après N jours d'inactivité (option).
```

### Commandes du bot

| Commande | Permission requise |
|---|---|
| `/optimus status` | `view_status` |
| `/optimus commands [recherche] [catégorie]` | `view_commands` |
| `/optimus command <nom>` (détail + binding) | `view_commands` |
| `/optimus history [n]` | `view_history` |
| `/optimus profiles` · `/optimus profile <id>` | `view_status` / `modify_config` |
| `/optimus say <texte>` | `execute_commands` |
| `/optimus exec <commande>` | `execute_commands` (+ guard local, + confirmation si `dangerous`) |
| `/optimus pair <code>` | — (c'est le point d'entrée) |
| `/optimus help` | — |

Alias courts : `/opt …`.

### Notifications (opt-in, événement par événement)

`🟢 Optimus démarré` · `🟡 Star Citizen détecté / fermé` · `🔵 commande exécutée` ·
`🔴 commande échouée` · `⚠️ commande inconnue` · `🟠 provider dégradé` · `⛔ kill switch activé`.

### Garde-fous supplémentaires

- `execute_commands` **désactivé par défaut**, y compris pour le propriétaire.
- Les commandes `dangerous` exigent une **confirmation dans l'application**, jamais sur Discord.
- Rate limit dédié, plus strict que l'API.
- Toute exécution d'origine Discord est marquée dans l'historique avec l'identité Discord.
- Le kill switch local coupe **aussi** les exécutions venues de Discord.
- Un utilisateur Discord ne peut être lié qu'à **une** instance à la fois (évite l'ambiguïté
  « quelle machine ? »).

---

## 12.3 Plugins

### Contrat

```csharp
// Optimus.Sdk — surface publique stable, versionnée (SemVer)
public interface IOptimusPlugin
{
    PluginMetadata Metadata { get; }                 // id, nom, version, sdk_version
    Task InitializeAsync(IPluginContext ctx, CancellationToken ct);
    Task ShutdownAsync(CancellationToken ct);
}

public interface IPluginContext
{
    IReadOnlyList<CommandDefinition> RegisterCommands();      // commandes apportées
    void RegisterActionHandler(string ns, IActionHandler h);  // type d'étape "plugin"
    void RegisterProvider<T>(T provider);                     // STT/TTS/LLM/GameState alternatif
    void RegisterCondition(string id, IConditionEvaluator e);
    IEventBus Events { get; }        // abonnement aux événements du cœur (lecture seule)
    ILogger Logger { get; }
    IPluginStorage Storage { get; }  // dossier et clés propres au plugin
    IPluginSettings Settings { get; }// schéma de réglages rendu automatiquement dans l'UI
}
```

### Modèle de permissions

Déclarées au manifeste, affichées à l'installation, refusables :

| Permission | Donne le droit de |
|---|---|
| `commands.register` | ajouter des commandes au catalogue |
| `commands.execute` | déclencher une commande existante |
| `providers.register` | fournir un STT/TTS/LLM/GameState |
| `network.outbound:<host>` | sortir vers un hôte précis (jamais `*` sans avertissement) |
| `filesystem.own` | écrire dans son propre dossier |
| `filesystem.read:<path>` | lire un chemin précis |
| `input.raw` | **injecter des entrées directement** — permission de plus haut niveau, avertissement explicite, réservée aux cas non couverts par les commandes |
| `events.subscribe` | écouter les événements du cœur |

Chargement dans un `AssemblyLoadContext` collectible (déchargement à chaud), dépendances isolées,
appels enveloppés (`try/catch` + timeout) : **un plugin qui plante ne fait pas tomber Optimus**.
Les packs distribués sont signés ; une signature invalide ⇒ installation refusée.

### Plugins de référence prévus

`starcitizen` (intégré au cœur au MVP, extrait en plugin ensuite) · `system` (volume, presse-papier,
lancement d'applications) · `spotify` · `obs` · `twitch` · `telemetry` · `voiceattack-import`.

---

## 12.4 Multi-copilotes

| Niveau | Fonctionnement | Version |
|---|---|---|
| **1 — Sélection** | Un copilote actif, changement à chaud (UI, voix, API) ; chacun a son wake word, sa voix, ses commandes | MVP (1 livré) / V1 (n) |
| **2 — Variantes** | Un même copilote décliné (`Optimus Lite/Combat/Mining`) par `enabled_commands` + capacités, sans code spécifique | V1 |
| **3 — Routage** | Le wake word détermine le destinataire : « Synthia, … » réveille Synthia même si Optimus est actif | V1/V2 |
| **4 — Multi-agents** | Plusieurs copilotes actifs, dialogues croisés, ordonnancement de parole, un seul détenteur du micro | V2 |

**Le problème dur du niveau 4** n'est pas technique mais scénique : deux voix qui se coupent
sont insupportables. Il faut un `ConversationDirector` (file de parole, priorités, tours,
interruptions autorisées ou non) — raison pour laquelle c'est du V2, pas du V1.

---

## 12.5 Packs `.optcopilot`

```
optimus-synthia-1.2.0.optcopilot   (ZIP signé)
├── manifest.json        id, nom, version, sdk_version, auteur, licence, checksum
├── copilot.json
├── personality.json
├── responses.fr.json / responses.en.json
├── commands/            commandes additionnelles (validées par schéma)
├── bindings/            suggestions de bindings (JAMAIS appliquées sans confirmation)
├── prompts/system.md    fragment borné et échappé
├── voices/              modèles Piper ou références de voix
├── assets/              avatar, sons
└── plugins/             optionnel, permissions déclarées
```

**Règles d'importation** (surface d'attaque à traiter au sérieux) :
1. Signature vérifiée ; sinon, avertissement explicite et installation manuelle.
2. Tout fichier validé par schéma **avant** écriture ; chemins normalisés (protection *zip slip*).
3. Les bindings suggérés sont proposés en **diff**, jamais appliqués silencieusement.
4. Le fragment de prompt est borné en longueur et ne peut pas contenir de directive de sécurité.
5. Les plugins embarqués suivent le circuit de permissions normal.
6. Import en bac à sable : le pack est d'abord chargé en **mode simulation forcé** pour un essai.
