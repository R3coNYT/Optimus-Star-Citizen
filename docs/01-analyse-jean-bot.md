# PHASE 1 — Analyse de Jean-Bot

> Méthode : lecture du HTML brut de `jean-bot.fr/index.php` et `jean-bot.fr/commandes.php`,
> extraction et lecture du JavaScript embarqué (~2 100 lignes, fichier unique de 132 Ko),
> appel direct des endpoints publics découverts dans ce JS, analyse statistique du catalogue JSON
> retourné. Aucun binaire n'a été téléchargé ni exécuté.
> Date de l'analyse : 2026-08-23. Site en évolution — ceci est un instantané.

Légende de fiabilité : **[C]** confirmé (observé directement) · **[D]** déduit (inférence
raisonnable à partir d'indices observés) · **[I]** inconnu (non accessible de l'extérieur).

---

## 1.1 Nature du produit

| Élément | Statut | Détail |
|---|---|---|
| Positionnement | **[C]** | « Packs de voix pour **Voice Attack** » (texte de la page d'accueil). Jean-Bot n'est donc pas un moteur autonome : c'est un **contenu** (profils + banques audio) pour VoiceAttack. |
| Slogan | **[C]** | « Le robot psychopathe qui vous accompagne dans Star Citizen » |
| Auteur | **[C]** | © 2026 Joffré Larrieu (streamer francophone « Joffre_Tiboy ») |
| Modèle économique | **[C]** | Achat unique, **sans abonnement**. Pack Premium 30 € (promo 20 € en début de mois, stock limité à 10 ex.). Démos gratuites. Page Steam + site officiel liés. |
| Volumétrie annoncée | **[C]** | Jean-Bot Premium « ~500 commandes », Synthia « 650 commandes et plus de 8 000 phrases », Virgil « 639 commandes ». |
| Production vocale | **[D]** | Voix humaines enregistrées (voix modifiée de l'auteur), **pas de TTS/IA** — cohérent avec « plus de 8 000 phrases » figées et avec les sources tierces. |
| STT | **[D]** | Aucune brique STT propriétaire : VoiceAttack utilise la reconnaissance vocale **Windows (SAPI)**. C'est le talon d'Achille du produit (précision, pas de NLU). |
| Compatibilité | **[C]** | « Star Citizen 4.9 », « Version Jean-Bot LIVE ». |

### Les trois copilotes **[C]**

| Copilote | Couleur d'identité | Rôle affiché | Tier max livré |
|---|---|---|---|
| Jean-Bot | rouge `#ef4444` | Compagnon de bord, humour noir | PREMIUM |
| Synthia | violet `#8b5cf6` | Assistante synthétique « élégante, claire, réactive », « puce comportementale activée » | PREMIUM |
| Virgil | bleu `#3b82f6` | Assistant militaire « précis, discipliné » | **STANDARD** (Premium annoncé mais pas encore servi par le catalogue) |

Le concept central de Jean-Bot est donc déjà **« un même socle, plusieurs personnalités »** —
c'est exactement ce qu'Optimus doit généraliser, mais de façon *paramétrique* plutôt que par
enregistrement manuel de 8 000 fichiers audio.

---

## 1.2 Architecture technique observée du site

**[C]** Stack : PHP côté serveur, page unique sans framework JS (vanilla + `<template>`),
Tailwind via CDN, FontAwesome 6.4, Google Fonts (Chakra Petch / Exo 2). PWA (`manifest.json`,
`display: standalone`, `start_url: ./commandes.php`, `orientation: portrait-primary`) → usage
**tablette / second écran** explicitement prévu.

### Endpoints réellement découverts **[C]**

| Endpoint | Méthode | Rôle |
|---|---|---|
| `auth.php?action=login` / `?action=logout` | GET (redirection) | OAuth **Discord**. URL obfusquée en base64 dans le `onclick`. |
| `get_commandes.php?bot={Jean-Bot\|Synthia\|Virgil}&tier={LITE\|STANDARD\|PREMIUM}` | GET | Catalogue de commandes en JSON. **Testé : répond 200 sans authentification** (262 Ko pour Jean-Bot/LITE). |
| `app/load_config.php` | GET (`credentials: include`) | Config utilisateur → `{success, config:{binds:{}, favorites:[], settings:{}}}`. Répond `{"success":false,"config":null}` hors session. |
| `app/save_config.php` | POST JSON | Persistance serveur des favoris (et vraisemblablement des binds). |
| `./{CODE}.json` | GET | Jeu-concours : un fichier JSON par code secret, contenant un **webhook Discord**. |
| `HUB/Jean-Bot HUB.exe`, `DEMOS/Jean-Bot HUB DEMO.exe` | GET | Launcher/installeur Windows propriétaire. URL en base64. |

**[D]** L'attribution des tiers passe par les **rôles Discord** : auth Discord + « Salon Discord
réservé aux Tipeurs » + message d'erreur de connexion proposant « Rejoindre le serveur Discord ».
Côté page, l'état est injecté en dur par PHP : `const BOT_TIERS = {'Jean-Bot':'LITE',
'Synthia':'LOCKED','Virgil':'LOCKED'}` et `MAX_AVAILABLE_TIERS` plafonne la demande.

**[I]** Fonctionnement interne du HUB (installation des profils VoiceAttack ? mise à jour ? DRM ?),
format des profils VoiceAttack livrés, logique serveur de `save_config.php`, gestion des licences.

---

## 1.3 Schéma de données du catalogue (extrait réel)

```json
{
  "catalog_version": "1.0",
  "total": 310,
  "commands": [{
    "id": 234550785755266,
    "code_name": "allumage_activation_du_systeme",
    "is_active": true,
    "is_hidden": false,
    "bot_targets": ["Jean-Bot"],
    "tier_required": "DEMO",
    "category": { "id": "avigation", "name": "Navigation" },
    "default_key": "R",
    "locales": { "fr_FR": {
        "name": "Allumage / Activation du système",
        "description": "Mettre sous tension l'ordinateur de bord et initialiser le HUD général." } },
    "metadata": { "actions_count": 1, "usage_count": 6 }
  }]
}
```

### Statistiques mesurées sur `Jean-Bot / LITE` (310 entrées) **[C]**

| Mesure | Valeur |
|---|---|
| Catégories | Navigation 102, Combat 93, Minage 89, Social 13, Exploration 11, LORE 2 |
| Tiers dans le fichier | LITE 298, DEMO 12 |
| `default_key` **non exécutables** | `Macro` ×73, `Dialogue` ×46, `Fiche` ×17, `Souris`, `HUD`, `F5/F6/F7`, `Échap`… |
| `actions_count == 0` | 192 / 310 (≈ 62 %) |
| Noms contenant des alias « / » | **269 / 310** |
| Locales | `fr_FR` uniquement (100 %) |
| `usage_count` | 0 → 312 |
| `category.id` | **toujours amputé du 1er caractère** : `avigation`, `ombat`, `inage`, `ocial`, `xploration`, `""` |

### Cinq enseignements majeurs

1. **`default_key` est un libellé d'affichage, pas un binding.** `"Macro"`, `"Dialogue"`,
   `"Fiche"`, `"HUD"` ne sont pas des touches. Le site est une **documentation**, pas un moteur :
   la véritable exécution vit dans le profil VoiceAttack, invisible et non éditable ici.
   → *Optimus doit faire l'inverse : le binding affiché EST le binding exécuté.*
2. **Les alias vocaux sont encodés dans le libellé**, séparés par `/` :
   « Allume / Active / Met / Remet le ressort caméra ». Il n'existe **aucun champ `aliases`
   structuré**, donc aucune recherche par phrase vocale, aucun scoring, aucune désambiguïsation.
   → *Optimus : `voice_phrases[]` first-class, indexées et normalisées.*
3. **46 « Dialogue » + 17 « Fiche » + catégorie LORE** : une part significative du catalogue
   n'exécute rien du tout — c'est du **contenu d'immersion pur**. C'est ce qui fait le charme du
   produit, et ce qu'un projet purement « macro » oublie systématiquement.
4. **`category.id` cassé sur 100 % des entrées** : le catalogue n'est pas normalisé côté serveur,
   le client compense avec une fonction `normalizeCategory()` qui remappe `combat/mining/minage/
   flight/navigation/social/socials/lore`. Signe d'un pipeline de génération artisanal
   (probablement export depuis VoiceAttack → transformation ad hoc).
   → *Optimus : catégories = énumération fermée, validée au chargement (schéma JSON).*
5. **Incohérence de volumétrie** : la page annonce en dur
   `COMMANDES_REELLES['Jean-Bot']['LITE'] = 246`, mais l'endpoint renvoie 310 entrées
   (298 LITE + 12 DEMO). Même en retirant les catégories verrouillées en LITE (Social 13 + LORE 2)
   on obtient 295, pas 246. **[I]** L'écart n'est pas explicable de l'extérieur — compteur
   marketing figé, ou filtrage serveur supplémentaire selon la session.

### Volumétrie déclarée dans le code de la page **[C]**

| Bot | Commandes LITE / STD / PREMIUM | Actions LITE / STD / PREMIUM |
|---|---|---|
| Jean-Bot | 246 / 447 / 642 | 1112 / 1313 / 1498 |
| Synthia | 246 / 443 / 666 | 1153 / 1724 / 1999 |
| Virgil | 251 / 442 / 646 | 1166 / 1739 / 1781 |

---

## 1.4 Fonctionnalités de l'interface `commandes.php`

**[C] Observées et lues dans le code :**

- **Sélecteur de copilote** (Jean-Bot / Synthia / Virgil) avec thème de couleur dynamique et logo.
- **Recherche** insensible aux accents, avec un **mode « mot strict »** astucieux : si la requête
  se termine par une espace, la correspondance passe en regex de mot entier. Porte sur le nom
  *et* la description.
- **Filtres de catégorie** : Tous, Combat, Minage, Exploration, Navigation, LORE, Social, Favoris.
- **Verrouillage par tier** : `categorieVerrouillee()` → LORE bloqué en LITE, Social bloqué en
  LITE et STANDARD, Favoris interdits en LITE.
- **Favoris** : étoile par commande, clé de stockage `botTarget::code_name`, persistés en
  `localStorage['jeanbot_favorites_v2']` **et** sur le serveur si session ouverte, avec
  **migration automatique** des anciens formats (`jeanbot_favorites_by_ia`, `jeanbot_favorites`)
  par rapprochement sur le libellé.
- **Mode HUD ON/OFF** : bascule vers un template de carte compact (`tmpl-hud-card`, LED, bordure
  colorée par catégorie) pour affichage sur second écran.
- **Mode SIMPIT ON/OFF** : plein écran + **Wake Lock API** (empêche la mise en veille) — pensé
  pour un cockpit physique / tablette encastrée.
- **Export** : `exportFavoritesToPDF()` — génère une « feuille de vol » PDF des favoris, groupée
  par catégorie, avec description et raccourci (perso s'il existe, sinon défaut).
- **Binds personnalisés** : `userBinds[botTarget::code_name]` est **lu** depuis `load_config.php`
  et affiché avec un marqueur « touche personnalisée »… mais **aucune UI d'édition n'existe sur
  cette page** → l'édition se fait ailleurs (HUB ? autre page ?) **[I]**.
- **Console avionique** repliable, système de toasts, bandeau d'incitation Discord.
- **« Terminal de Liaison Quantique »** : jeu-concours. L'utilisateur saisit un code → le client
  fait `fetch('./{CODE}.json')` → le JSON contient un **webhook Discord** → le client POSTe
  pseudo + e-mail directement dessus. Verrou anti-rejeu de 24 h en `localStorage`.

> ⚠️ **Anti-pattern de sécurité à ne surtout pas reproduire.** Le webhook Discord est livré au
> navigateur : quiconque possède le code peut le spammer indéfiniment, et les fichiers de code
> sont énumérables. L'anti-rejeu est côté client donc contournable, et des e-mails d'utilisateurs
> transitent vers un webhook exposé. **Leçon pour Optimus : aucun secret (webhook, token, clé API)
> ne doit jamais atteindre un client non fiable ; tout quota / anti-rejeu est serveur.**

---

## 1.5 Analyse UX

### Ce qui fonctionne — à reprendre

| Point fort | Pourquoi ça marche | Transposition Optimus |
|---|---|---|
| Identité visuelle cockpit/avionique cohérente | On *croit* au produit avant de l'essayer ; l'immersion commence sur le site | Design system « avionique » unique, partagé app + web |
| Un copilote = une couleur + un logo + une voix | Identification immédiate, donne envie de collectionner | `Copilot` porte son thème (`accent_color`, `avatar`) |
| Mode HUD + SIMPIT + PWA | Reconnaît l'usage réel : second écran, tablette, cockpit physique | Mode HUD/overlay + API locale consommable par une tablette |
| Favoris + export « feuille de vol » PDF | Le joueur ne retient pas 500 commandes : il en utilise 20 | Favoris + cheat-sheet imprimable, générée depuis les *vrais* binds |
| Recherche avec mode mot strict | Détail malin, gros gain sur un catalogue de 600 entrées | À reprendre tel quel dans le Command Browser |
| Dialogues & Lore (63 entrées sans action) | C'est ça, « avoir un copilote » — pas un presse-bouton | `Command.kind: action \| macro \| dialogue \| lore` dès le modèle |
| Achat unique, sans abonnement | Aligné avec la culture Star Citizen | Optimus = local, sans compte obligatoire |

### Ce qui coince — à améliorer

| Faiblesse | Impact | Réponse d'Optimus |
|---|---|---|
| Dépendance à VoiceAttack + Windows Speech | Précision médiocre, phrases figées à réciter mot pour mot, pas de NLU | STT neural local (Whisper) + matcher flou + LLM optionnel |
| Voix = fichiers WAV pré-enregistrés | Non modifiable, non traduisible, non extensible : chaque réplique = une session de studio | TTS neural, `VoiceProvider` interchangeable, réponses issues de la personnalité |
| `default_key` purement décoratif | L'utilisateur voit « Macro » et ne sait pas quoi faire ; s'il rebinde dans SC, tout casse silencieusement | Binding réel, importé du XML SC, éditable, avec détection de conflits |
| Alias noyés dans le libellé | Pas de recherche par phrase, pas de scoring, pas d'alias perso | `voice_phrases[]` + alias utilisateur + apprentissage des phrases non reconnues |
| Mono-langue (`fr_FR` seul) | Marché limité | i18n par copilote dès le modèle de données |
| Gating agressif (favoris interdits en LITE) | Punit sur une fonction *de confort*, pas sur la valeur | Aucune fonction de confort verrouillée |
| Catalogue non normalisé (`category.id` cassé) | Dette silencieuse, bugs d'affichage | Schéma JSON validé au chargement, lint du catalogue en CI |
| Pas de contexte conversationnel | Chaque phrase est isolée : impossible de dire « et à l'avant » | `ConversationContext` + résolution d'anaphores |
| Pas de retour d'échec explicite | Si la commande n'est pas comprise : silence | Jamais d'échec silencieux (voir RF-ERR) |
| Webhook exposé côté client | Faille exploitable | Aucun secret côté client, quotas serveur |

### Ce qu'Optimus peut faire que Jean-Bot ne peut structurellement pas

1. **Comprendre au lieu de reconnaître** — Whisper + normalisation + matcher flou tolèrent
   « ouvre-moi les portes stp », là où Windows Speech exige la phrase exacte.
2. **Exécuter ce qui est affiché** — import du `layout_*.xml` de Star Citizen : les binds
   d'Optimus *sont* ceux du joueur, et un rebind dans le jeu se resynchronise.
3. **Personnalité paramétrique** — curseurs (humour, sarcasme, formalisme…) au lieu de 8 000 WAV.
4. **Extensibilité** — plugins, macros conditionnelles, Command Builder sans code.
5. **Offline et gratuit à l'usage** — zéro appel réseau obligatoire, zéro coût API.
6. **Transparence** — mode debug qui montre STT → intent → confiance → binding → exécution ;
   mode simulation qui n'appuie sur rien.
