# OPTIMUS

**Copilote vocal IA pour Star Citizen.** Application Windows locale : Optimus écoute, comprend,
exécute les actions du jeu via *tes* raccourcis, et répond avec une voix et une personnalité
configurables.

```
voix → STT local → intent → validation → binding local → clavier local → Star Citizen
                                    ↑
                        LLM optionnel (jamais requis, jamais aux commandes)
```

## Installation

Prends l'installateur dans [les versions publiées](../../releases) et lance-le. Installation par
utilisateur, sans droits administrateur, dans `%LOCALAPPDATA%\Programs\Optimus`.

Une page de l'assistant propose les composants facultatifs — voix neuronales et parole libre —
qui se téléchargent alors, chacun vérifié par son empreinte. Sans eux, Optimus fonctionne
entièrement : voix Windows et grammaire fermée.

> **L'installateur n'est pas encore signé.** SmartScreen affichera « Windows a protégé votre
> ordinateur » : le bouton pour continuer se cache derrière *Informations complémentaires*. La
> signature est en cours, voir plus bas. Vérifie l'empreinte SHA-256 publiée à côté du fichier.

## Statut

**Application complète, moteur éprouvé.** Les spikes de risque sont passés (`docs/13`) :
l'injection scancode est validée dans le jeu, les 627 bindings réels de Star Citizen 4.9 sont
importés, et le pipeline `énoncé → intention → garde → binding → entrées` tourne de bout en bout.

L'application de bureau couvre l'écoute, le catalogue, les touches, les macros, les réglages et
ce qu'Optimus n'a pas compris. Un banc d'essai en ligne de commande double le tout, sans micro,
sans clavier et sans le jeu.

```bash
dotnet run --project tools/Optimus.Cli -- "Optimus, allume les lumieres"
```

```
trace f06bb79f · Simulated · 13.5 ms
  énoncé      « Optimus allume les lumieres »
  normalisé   « allume les lumieres »
  intent      ship.lights.toggle  score 1.00  (Exact)
  garde       Allowed
  étape 0     lights_controller/v_lights → L
```

Sans argument, le programme passe en mode interactif ; `?` liste les commandes et leurs touches,
`--status` détaille la détection du jeu. `dotnet test` exécute la suite contre les données réelles
du dépôt.

**La simulation est le mode par défaut** : aucune touche ne part tant que `--real` n'est pas
demandé explicitement. Le mode réel exige que Star Citizen soit lancé, au premier plan, et refuse
de démarrer si le jeu est élevé alors qu'Optimus ne l'est pas — Windows filtrerait les entrées
sans rien dire.

```bash
dotnet run --project tools/Optimus.Cli -- --real "Optimus, allume les lumieres"
```

## Parler à Optimus

```bash
dotnet run --project tools/Optimus.Cli -- --listen
```

Optimus écoute, reconnaît la commande, l'exécute et répond à voix haute.

Deux modes, réglés dans [`data/profiles/default.json`](data/profiles/default.json) :

| Mode | Déclenchement | Grammaire |
|---|---|---|
| `always_on` *(défaut)* | le mot d'éveil | uniquement `Optimus <commande>` |
| `push_to_talk` | une touche configurable | les deux formes, désactivée hors appui |

En écoute permanente, la grammaire n'accepte que les phrases commençant par « Optimus » :
une conversation ordinaire ne correspond à aucune alternative et n'est pas même transcrite.

## Architecture

| Projet | Cible | Rôle |
|---|---|---|
| `Optimus.Core` | `net8.0` **neutre** | Domaine, intentions, exécution, simulation. Aucune API système : testable partout. |
| `Optimus.Infrastructure` | `net8.0-windows` | `SendInput` en scancodes, table de touches, détection du jeu. |
| `Optimus.Cli` | `net8.0-windows` | Banc d'essai du moteur. |

## Principes

- **Local d'abord** : fonctionne hors ligne, sans compte, sans coût d'API.
- **Zéro keybind en dur** : les touches sont importées de Star Citizen et éditables.
- **L'IA propose, le moteur dispose** : le LLM ne peut émettre qu'un intent d'une liste blanche.
- **Isolation par machine** : une commande n'agit que sur le PC qui l'a reçue.
- **Autonome** : aucune dépendance à VoiceAttack.
- **Un copilote est une donnée** : personnalité, voix, capacités et commandes sont des fichiers.

## Ce qu'Optimus ne fera pas

Pas d'automatisation continue du gameplay (visée, farming, macros en boucle), pas de contrôle
d'une machine tierce, pas de télémétrie sans consentement. Un énoncé vocal = une action
délibérée du joueur.

## Documentation

Voir [docs/00-INDEX.md](docs/00-INDEX.md) — l'analyse, l'architecture, la stack, les modèles de
données, le moteur de commandes, la personnalité, le pipeline vocal, l'interface, la roadmap, les
risques et les décisions.

## Signature de code

Les binaires publiés seront signés par la [SignPath Foundation](https://signpath.org), qui offre
ce service aux projets libres. La **politique de signature** — qui signe, ce qui est signé, ce qui
ne l'est jamais — vit dans [docs/14](docs/14-signature-de-code.md).

Rien n'est signé depuis un poste de développement : seule la [chaîne de
compilation](.github/workflows/release.yml), déclenchée par un tag de ce dépôt, peut soumettre un
artefact à la signature.

## Licence

[GNU General Public License v3.0](LICENSE). Tu peux utiliser, étudier, modifier et redistribuer
Optimus ; toute version redistribuée doit rester libre sous la même licence.

Les composants facultatifs téléchargés à l'installation ne sont ni recompilés ni redistribués par
ce dépôt, et gardent leur propre licence : [Piper](https://github.com/rhasspy/piper) (MIT),
[whisper.cpp](https://github.com/ggml-org/whisper.cpp) (MIT) et leurs modèles.

---

*Optimus est un projet indépendant, sans lien avec Cloud Imperium Games. Star Citizen est une
marque de Cloud Imperium Rights LLC. Aucun fichier du jeu n'est redistribué ici : Optimus lit les
raccourcis que tu exportes toi-même depuis le jeu.*
