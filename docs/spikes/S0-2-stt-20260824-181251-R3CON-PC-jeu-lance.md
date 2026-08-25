# Spike S0-2 - transcription Whisper

*R3CON-PC - 2026-08-24 18:12:51*

Binaire : `D:\app\80-Star Citizen\Optimus\whisper\whisper-bin-x64\Release\whisper-cli.exe`

## Synthèse

| Modèle | Mesures | Chargement (ms) | Inférence p50 (ms) | Inférence p95 (ms) | RTF moyen | WER moyen (%) | Cible p95 <= 500 ms |
|---|---|---|---|---|---|---|---|
| ggml-base | 24 | 596 | 5388.9 | 5948.4 | 2.277 | 7.1 | non |
| ggml-small | 24 | 1716 | 19091.1 | 21175.3 | 8.317 | 13.3 | non |

Le chargement est payé une seule fois au démarrage d Optimus ; seule l inférence entre
dans le budget de latence de docs/09.

## Transcriptions

| Modèle | Fichier | Attendu | Transcription | WER (%) |
|---|---|---|---|---|
| ggml-base | utt01-R3CON-PC.wav | Optimus, ouvre les portes | Optimus ouvre les portes. | 0 |
| ggml-base | utt02-R3CON-PC.wav | Optimus, prépare le saut quantique | Optimus prépare le saut quantique. | 0 |
| ggml-base | utt03-R3CON-PC.wav | Optimus, mets les boucliers sur l'avant | Optimus, mets les bouquillés sur l'avant. | 16.7 |
| ggml-base | utt04-R3CON-PC.wav | Optimus, active le scan | Optimus active le scan. | 0 |
| ggml-base | utt05-R3CON-PC.wav | Optimus, rapport système | Optimus Rapport Système | 0 |
| ggml-base | utt06-R3CON-PC.wav | Optimus, passe en mode combat | Optimus passe sur notre combat. | 40 |
| ggml-base | utt07-R3CON-PC.wav | Optimus, allume les moteurs | Optimus allume les moteurs. | 0 |
| ggml-base | utt08-R3CON-PC.wav | Optimus, tu penses qu'on devrait se poser ? | Optimus, tu penses qu'on devrait se poser ? | 0 |
| ggml-small | utt01-R3CON-PC.wav | Optimus, ouvre les portes | Optimus ouvre les portes. | 0 |
| ggml-small | utt02-R3CON-PC.wav | Optimus, prépare le saut quantique | Optimus prépare le saut quantique. | 0 |
| ggml-small | utt03-R3CON-PC.wav | Optimus, mets les boucliers sur l'avant | Optimus, mets les bouts qui y est sur l'avant. | 66.7 |
| ggml-small | utt04-R3CON-PC.wav | Optimus, active le scan | Optimus active le scan. | 0 |
| ggml-small | utt05-R3CON-PC.wav | Optimus, rapport système | Optimus rapport système | 0 |
| ggml-small | utt06-R3CON-PC.wav | Optimus, passe en mode combat | Optimus, passant mode combat. | 40 |
| ggml-small | utt07-R3CON-PC.wav | Optimus, allume les moteurs | Optimus allume les moteurs. | 0 |
| ggml-small | utt08-R3CON-PC.wav | Optimus, tu penses qu'on devrait se poser ? | Optimus, tu penses qu'on devrait se poser ? | 0 |

## Détail

| Modèle | Fichier | Passage | Audio (ms) | Chargement (ms) | Inférence (ms) | RTF |
|---|---|---|---|---|---|---|
| ggml-base | utt01-R3CON-PC.wav | 1 | 2100 | 1119.7 | 3951.1 | 1.881 |
| ggml-base | utt01-R3CON-PC.wav | 2 | 2100 | 424.2 | 3421 | 1.629 |
| ggml-base | utt01-R3CON-PC.wav | 3 | 2100 | 457.1 | 3285.5 | 1.565 |
| ggml-base | utt02-R3CON-PC.wav | 1 | 2500 | 434.8 | 4362.9 | 1.745 |
| ggml-base | utt02-R3CON-PC.wav | 2 | 2500 | 388.9 | 4973.1 | 1.989 |
| ggml-base | utt02-R3CON-PC.wav | 3 | 2500 | 851.8 | 7482.4 | 2.993 |
| ggml-base | utt03-R3CON-PC.wav | 1 | 2800 | 451.8 | 5397.5 | 1.928 |
| ggml-base | utt03-R3CON-PC.wav | 2 | 2800 | 567.2 | 5948.4 | 2.124 |
| ggml-base | utt03-R3CON-PC.wav | 3 | 2800 | 604.4 | 5705.6 | 2.038 |
| ggml-base | utt04-R3CON-PC.wav | 1 | 2100 | 408.2 | 5026.2 | 2.393 |
| ggml-base | utt04-R3CON-PC.wav | 2 | 2100 | 675.7 | 5443.1 | 2.592 |
| ggml-base | utt04-R3CON-PC.wav | 3 | 2100 | 491.3 | 5794.1 | 2.759 |
| ggml-base | utt05-R3CON-PC.wav | 1 | 2100 | 1008 | 5247.6 | 2.499 |
| ggml-base | utt05-R3CON-PC.wav | 2 | 2100 | 502.1 | 5087.1 | 2.422 |
| ggml-base | utt05-R3CON-PC.wav | 3 | 2100 | 497.3 | 5723.7 | 2.726 |
| ggml-base | utt06-R3CON-PC.wav | 1 | 2100 | 637.5 | 5525 | 2.631 |
| ggml-base | utt06-R3CON-PC.wav | 2 | 2100 | 624.2 | 5602.5 | 2.668 |
| ggml-base | utt06-R3CON-PC.wav | 3 | 2100 | 737.7 | 4692.1 | 2.234 |
| ggml-base | utt07-R3CON-PC.wav | 1 | 2100 | 536.7 | 5057.1 | 2.408 |
| ggml-base | utt07-R3CON-PC.wav | 2 | 2100 | 661.8 | 4482.7 | 2.135 |
| ggml-base | utt07-R3CON-PC.wav | 3 | 2100 | 715.8 | 5388.9 | 2.566 |
| ggml-base | utt08-R3CON-PC.wav | 1 | 2300 | 514.1 | 5556.6 | 2.416 |
| ggml-base | utt08-R3CON-PC.wav | 2 | 2300 | 534.8 | 5527.3 | 2.403 |
| ggml-base | utt08-R3CON-PC.wav | 3 | 2300 | 452.4 | 4372.8 | 1.901 |
| ggml-small | utt01-R3CON-PC.wav | 1 | 2100 | 3864.8 | 19253 | 9.168 |
| ggml-small | utt01-R3CON-PC.wav | 2 | 2100 | 1717.8 | 20710.3 | 9.862 |
| ggml-small | utt01-R3CON-PC.wav | 3 | 2100 | 1815.1 | 19635.2 | 9.35 |
| ggml-small | utt02-R3CON-PC.wav | 1 | 2500 | 1480.1 | 21175.3 | 8.47 |
| ggml-small | utt02-R3CON-PC.wav | 2 | 2500 | 1559.7 | 19828.9 | 7.932 |
| ggml-small | utt02-R3CON-PC.wav | 3 | 2500 | 1612.9 | 19449.4 | 7.78 |
| ggml-small | utt03-R3CON-PC.wav | 1 | 2800 | 1483.6 | 22380.8 | 7.993 |
| ggml-small | utt03-R3CON-PC.wav | 2 | 2800 | 1477.7 | 20603.1 | 7.358 |
| ggml-small | utt03-R3CON-PC.wav | 3 | 2800 | 1428.3 | 21093.5 | 7.533 |
| ggml-small | utt04-R3CON-PC.wav | 1 | 2100 | 1948.9 | 20500.3 | 9.762 |
| ggml-small | utt04-R3CON-PC.wav | 2 | 2100 | 1966.4 | 19326.8 | 9.203 |
| ggml-small | utt04-R3CON-PC.wav | 3 | 2100 | 1645.7 | 16877.9 | 8.037 |
| ggml-small | utt05-R3CON-PC.wav | 1 | 2100 | 1436.2 | 17416.6 | 8.294 |
| ggml-small | utt05-R3CON-PC.wav | 2 | 2100 | 1609.4 | 18055.9 | 8.598 |
| ggml-small | utt05-R3CON-PC.wav | 3 | 2100 | 2292.2 | 18353.3 | 8.74 |
| ggml-small | utt06-R3CON-PC.wav | 1 | 2100 | 1824.3 | 16954.3 | 8.073 |
| ggml-small | utt06-R3CON-PC.wav | 2 | 2100 | 1602.8 | 18322.6 | 8.725 |
| ggml-small | utt06-R3CON-PC.wav | 3 | 2100 | 1706.7 | 16712.3 | 7.958 |
| ggml-small | utt07-R3CON-PC.wav | 1 | 2100 | 1406.8 | 17831.6 | 8.491 |
| ggml-small | utt07-R3CON-PC.wav | 2 | 2100 | 1612.2 | 15715.3 | 7.483 |
| ggml-small | utt07-R3CON-PC.wav | 3 | 2100 | 1427.4 | 14830.9 | 7.062 |
| ggml-small | utt08-R3CON-PC.wav | 1 | 2300 | 1425.5 | 18567.1 | 8.073 |
| ggml-small | utt08-R3CON-PC.wav | 2 | 2300 | 1510.2 | 16910.4 | 7.352 |
| ggml-small | utt08-R3CON-PC.wav | 3 | 2300 | 1320.5 | 19091.1 | 8.3 |
