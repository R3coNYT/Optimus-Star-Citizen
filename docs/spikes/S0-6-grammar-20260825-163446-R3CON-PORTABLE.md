# Spike S0-6 - reconnaissance a grammaire contrainte

*R3CON-PORTABLE - 2026-08-25 16:34:46*

Moteur : `MS-1036-80-DESK` (fr-FR)

| | |
|---|---|
| Alternatives dans la grammaire | 414 |
| Commandes couvertes | 59 |
| Chargement de la grammaire | 39 ms (une seule fois) |
| Seuil de confiance | 0.35 |
| Latence p50 | **16.7 ms** |
| Latence p95 | **27.2 ms** |
| Bonne commande identifiee | **21 / 21** (100 %) |
| dont acceptees au seuil 0.35 | 21 |
| dont rejetees par le seuil | 0 |
| Meprises (action non demandee) | **0** |
| Aucune reconnaissance | 0 |

## Calibrage du seuil

| Seuil | Commandes acceptees | Faux declenchements |
|---|---|---|
| 0.2 | 21 / 21 | 0 |
| 0.3 | 21 / 21 | 0 |
| 0.35 | 21 / 21 | 0 |
| 0.4 | 21 / 21 | 0 |
| 0.45 | 15 / 21 | 0 |
| 0.5 | 12 / 21 | 0 |
| 0.6 | 9 / 21 | 0 |
| 0.7 | 3 / 21 | 0 |

Comparaison S0-2 (Whisper base, jeu lance) : p50 3336 ms, WER 9,8 %.
Un moteur a grammaire ne peut produire qu une phrase autorisee : la metrique
pertinente est la commande resolue, pas le texte exact.

## Detail

| Fichier | Passage | Latence (ms) | Confiance | Reconnu | Commande | Attendue | Correct |
|---|---|---|---|---|---|---|---|
| utt01-R3CON-PC.wav | 1 | 58.4 | 0.58 | optimus ouvre les portes | ship.doors.toggle | ship.doors.toggle | oui |
| utt01-R3CON-PC.wav | 2 | 18.7 | 0.58 | optimus ouvre les portes | ship.doors.toggle | ship.doors.toggle | oui |
| utt01-R3CON-PC.wav | 3 | 16.7 | 0.58 | optimus ouvre les portes | ship.doors.toggle | ship.doors.toggle | oui |
| utt02-R3CON-PC.wav | 1 | 19.4 | 0.436 | optimus prepare le saut quantique | quantum.engage | quantum.engage | oui |
| utt02-R3CON-PC.wav | 2 | 16.7 | 0.436 | optimus prepare le saut quantique | quantum.engage | quantum.engage | oui |
| utt02-R3CON-PC.wav | 3 | 15.3 | 0.436 | optimus prepare le saut quantique | quantum.engage | quantum.engage | oui |
| utt03-R3CON-PC.wav | 1 | 19.7 | 0.739 | optimus mets les boucliers sur l'avant | shields.raise.front | shields.raise.front | oui |
| utt03-R3CON-PC.wav | 2 | 19.5 | 0.739 | optimus mets les boucliers sur l'avant | shields.raise.front | shields.raise.front | oui |
| utt03-R3CON-PC.wav | 3 | 25.3 | 0.739 | optimus mets les boucliers sur l'avant | shields.raise.front | shields.raise.front | oui |
| utt04-R3CON-PC.wav | 1 | 13.8 | 0.401 | optimus active le scan | scan.mode.toggle | scan.mode.toggle | oui |
| utt04-R3CON-PC.wav | 2 | 15.6 | 0.401 | optimus active le scan | scan.mode.toggle | scan.mode.toggle | oui |
| utt04-R3CON-PC.wav | 3 | 13.1 | 0.401 | optimus active le scan | scan.mode.toggle | scan.mode.toggle | oui |
| utt05-R3CON-PC.wav | 1 | 16.2 | 0.466 | optimus rapport systeme | system.status | system.status | oui |
| utt05-R3CON-PC.wav | 2 | 18.8 | 0.466 | optimus rapport systeme | system.status | system.status | oui |
| utt05-R3CON-PC.wav | 3 | 15.1 | 0.466 | optimus rapport systeme | system.status | system.status | oui |
| utt06-R3CON-PC.wav | 1 | 14.9 | 0.68 | optimus passe en mode combat | nav.master_mode.cycle | nav.master_mode.cycle | oui |
| utt06-R3CON-PC.wav | 2 | 14.9 | 0.68 | optimus passe en mode combat | nav.master_mode.cycle | nav.master_mode.cycle | oui |
| utt06-R3CON-PC.wav | 3 | 13.7 | 0.68 | optimus passe en mode combat | nav.master_mode.cycle | nav.master_mode.cycle | oui |
| utt07-R3CON-PC.wav | 1 | 11.7 | 0.656 | optimus allume les moteurs | ship.engines.toggle | ship.engines.toggle | oui |
| utt07-R3CON-PC.wav | 2 | 11 | 0.656 | optimus allume les moteurs | ship.engines.toggle | ship.engines.toggle | oui |
| utt07-R3CON-PC.wav | 3 | 11.2 | 0.656 | optimus allume les moteurs | ship.engines.toggle | ship.engines.toggle | oui |
| utt08-R3CON-PC.wav | 1 | 27.2 | 0 |  |  |  | non |
| utt08-R3CON-PC.wav | 2 | 25.5 | 0 |  |  |  | non |
| utt08-R3CON-PC.wav | 3 | 22.8 | 0 |  |  |  | non |
