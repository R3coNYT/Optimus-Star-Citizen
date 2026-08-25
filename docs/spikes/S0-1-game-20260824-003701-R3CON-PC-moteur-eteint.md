# Spike S0-1 — injection d'entrées (game)

*Généré par Optimus.Spike.InputTest 1.0.0 le 2026-08-24 00:37:01*

## Environnement

| | |
|---|---|
| Machine | R3CON-PC |
| Système | Microsoft Windows NT 10.0.26200.0 (x64) |
| Runtime | 4.0.30319.42000 |
| Élévation du spike | utilisateur standard |
| Sonde hook (WH_KEYBOARD_LL) | active |
| Sonde Raw Input (WM_INPUT) | active |
| Star Citizen | StarCitizen (pid 19920), élévation : non |
| Fenêtre au premier plan au démarrage | WindowsTerminal (pid 14884) — "Windows PowerShell" |

## Synthèse

| Test | Verdict | Intitulé |
|---|---|---|
| G1 | **WARN** | Appui scancode sur L |
| G2 | **WARN** | Appui virtual-key seul sur L |
| G3 | **WARN** | Appui très court (16 ms) sur L |
| G4 | **WARN** | Maintien 800 ms sur SPACE |
| G5 | **WARN** | Combinaison LALT + L |
| G7 | **WARN** | Bouton souris latéral X2 |

## Détail

### G1 — Appui scancode sur L

**Verdict : WARN**

Question : Le jeu a-t-il réagi à la touche L ?

Observation : ?

Tap scancode 0x26, maintien 45 ms.
Injection exécutée en 62,7 ms, 2 évènement(s) confirmé(s) par les sondes.

### G2 — Appui virtual-key seul sur L

**Verdict : WARN**

Question : Le jeu a-t-il réagi CETTE FOIS (méthode virtual-key) ?

Observation : ?

Tap vk=0x4C sans scancode.
Injection exécutée en 46,4 ms, 2 évènement(s) confirmé(s) par les sondes.

### G3 — Appui très court (16 ms) sur L

**Verdict : WARN**

Question : Le jeu a-t-il réagi à l'appui de 16 ms ?

Observation : ?

Tap scancode, maintien 16 ms — cherche la limite basse acceptée par le jeu.
Injection exécutée en 17,0 ms, 2 évènement(s) confirmé(s) par les sondes.

### G4 — Maintien 800 ms sur SPACE

**Verdict : WARN**

Question : Le maintien a-t-il été pris en compte pendant toute sa durée ?

Observation : ?

Down, 800 ms, up.
Injection exécutée en 801,2 ms, 2 évènement(s) confirmé(s) par les sondes.

### G5 — Combinaison LALT + L

**Verdict : WARN**

Question : La combinaison a-t-elle déclenché l'action attendue ?

Observation : ?

Modificateur maintenu, touche tapée, modificateur relâché.
Injection exécutée en 77,6 ms, 4 évènement(s) confirmé(s) par les sondes.

### G7 — Bouton souris latéral X2

**Verdict : WARN**

Question : Le jeu a-t-il réagi au bouton latéral (s'il est assigné) ?

Observation : ?

Down/up sur le bouton X2.
Injection exécutée en 49,0 ms, 0 évènement(s) confirmé(s) par les sondes.

## Conclusion à tirer

- **T1/G1 en PASS** → l'injection scancode est la bonne approche : le plan A d'`docs/05` (D10) est validé.
- **G1 en FAIL** → risque R1 confirmé : passer au plan B (pilote Interception) avant de continuer.
- **G2 en FAIL alors que G1 est PASS** → confirme que `KEYEVENTF_SCANCODE` est obligatoire.
- **T7 avec divergences** → interdiction formelle d'utiliser MapVirtualKey dans le moteur.
- **T4** → fixe la valeur par défaut de `hold_ms` dans le `SequenceRunner`.

