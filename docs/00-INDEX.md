# OPTIMUS — Dossier de conception

Copilote vocal IA pour Star Citizen. Application Windows locale, autonome, extensible.

## Sommaire

| Doc | Phase du brief | Contenu |
|---|---|---|
| [01 — Analyse de Jean-Bot](01-analyse-jean-bot.md) | Phase 1 | Fonctionnalités confirmées / déduites / inconnues, endpoints réels, schéma du catalogue, analyse UX, ce qu'Optimus fait mieux |
| [02 — Keybinds Star Citizen](02-analyse-keybinds-star-citizen.md) | Phase 1bis | Analyse de l'export XML fourni, format `ActionMaps`, lecture de la capture d'écran, stratégie de binding |
| [03 — Besoins](03-besoins.md) | Phase 2 | Exigences fonctionnelles, non fonctionnelles, techniques, de sécurité, UX |
| [04 — Architecture](04-architecture.md) | Phase 3 | Couches, responsabilités, modèle de processus, flux nominal, isolation par utilisateur, nomenclature |
| [05 — Stack technique](05-stack.md) | Phase 4 | Comparatif des socles, décisions par composant, points de vigilance |
| [06 — Modèle de données](06-modele-donnees.md) | Phase 5 | ERD, arborescence utilisateur, schémas JSON, SQLite, migrations |
| [07 — Moteur de commandes](07-moteur-commandes.md) | Phase 6 | Command / Intent / Action / Binding / Sequence / Macro / Condition / Response, résolution, contrat de l'IA |
| [08 — Personnalité](08-personnalite.md) | Phase 7 | Traits, sélection de réponse, règles, prompt généré, trois personnalités de référence |
| [09 — Pipeline vocal](09-pipeline-vocal.md) | Phase 8 | Chaîne complète, budget de latence, modes d'écoute, providers, pièges audio, mode debug |
| [10 — Interface](10-interface.md) | Phase 9 | Les 12 écrans, maquettes, assistant de premier lancement |
| [11 — Roadmap](11-roadmap.md) | Phases 10–12 | MVP v0.1 (périmètre, définition de terminé, planning), V1, V2, non-objectifs |
| [12 — API / Discord / Plugins](12-api-discord-plugins.md) | transverse | API locale, stratégie Discord et isolation, modèle de plugins, multi-copilotes, packs |
| [13 — Risques, tests, décisions](13-risques-tests-decisions.md) | transverse | Registre des risques, spikes préalables, stratégie de tests, 18 décisions, points à trancher |
| [14 — Structure du projet](14-structure-projet.md) | Phase 77 | Arborescence de la solution, graphe de dépendances, conventions |
| [15 — Signature de code](15-signature-de-code.md) | transverse | Conditions de la SignPath Foundation, chaîne de compilation, politique de signature, ce qu'il reste à faire |

## Les dix règles non négociables

1. **Aucun keybind en dur.** Les touches viennent d'un `BindingProfile` chargé au runtime.
2. **Le LLM est optionnel** et désactivé par défaut ; tout fonctionne hors ligne.
3. **L'IA ne produit qu'un intent structuré**, validé contre une liste blanche ; elle n'a jamais
   accès au clavier.
4. **Un seul point de contrôle** (`ExecutionGuard`) pour permissions, kill switch, simulation,
   cooldown et focus.
5. **L'exécution est toujours locale.** Discord et le cloud transmettent des intents, jamais des
   touches.
6. **Jamais d'échec silencieux** : toute erreur produit une réponse et une trace.
7. **Le mode simulation existe dès le premier jour.**
8. **La configuration utilisateur vit dans `%APPDATA%`**, en fichiers versionnables.
9. **Le cœur est testable sans micro, sans clavier et sans le jeu.**
10. **Un copilote est une donnée**, pas du code : créer « Optimus Combat » ne demande aucune
    ligne de C#.
