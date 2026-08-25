# Spike S0-2 - transcription Whisper

*R3CON-PC - 2026-08-24 18:24:15*

Binaire : `D:\app\80-Star Citizen\Optimus\whisper\whisper-bin-x64\Release\whisper-cli.exe`

## Synthèse

| Modèle | Mesures | Chargement (ms) | Inférence p50 (ms) | Inférence p95 (ms) | RTF moyen | WER moyen (%) | Cible p95 <= 500 ms |
|---|---|---|---|---|---|---|---|
| ggml-base | 24 | 598 | 5166 | 5912.6 | 2.453 | 9.8 | non |
| ggml-small | 24 | 1551 | 17454.6 | 19890.1 | 8.413 | 10.4 | non |

Le chargement est payé une seule fois au démarrage d Optimus ; seule l inférence entre
dans le budget de latence de docs/09.

## Transcriptions

| Modèle | Fichier | Attendu | Transcription | WER (%) |
|---|---|---|---|---|
| ggml-base | utt01-R3CON-PC.wav | Optimus, ouvre les portes | Optimus ouvre les portes. | 0 |
| ggml-base | utt02-R3CON-PC.wav | Optimus, prépare le saut quantique | Optimus prépare le saut quantique. | 0 |
| ggml-base | utt03-R3CON-PC.wav | Optimus, mets les boucliers sur l'avant | Optimus, mets les bouquillers sur l'avant. | 16.7 |
| ggml-base | utt04-R3CON-PC.wav | Optimus, active le scan | Optimus active le scan. | 0 |
| ggml-base | utt05-R3CON-PC.wav | Optimus, rapport système | Optimus Rapport System | 33.3 |
| ggml-base | utt06-R3CON-PC.wav | Optimus, passe en mode combat | Optimus passe en mode combat. | 0 |
| ggml-base | utt07-R3CON-PC.wav | Optimus, allume les moteurs | Optimus allume les moteurs. | 0 |
| ggml-base | utt08-R3CON-PC.wav | Optimus, tu penses qu'on devrait se poser ? | Optimus, tu penses qu'on devrait esposer ? | 28.6 |
| ggml-small | utt01-R3CON-PC.wav | Optimus, ouvre les portes | Optimus ouvre les portes. | 0 |
| ggml-small | utt02-R3CON-PC.wav | Optimus, prépare le saut quantique | Optimus prépare le saut quantique. | 0 |
| ggml-small | utt03-R3CON-PC.wav | Optimus, mets les boucliers sur l'avant | Optimus met les bouts qui y est sur l'avant. | 83.3 |
| ggml-small | utt04-R3CON-PC.wav | Optimus, active le scan | Optimus active le scan. | 0 |
| ggml-small | utt05-R3CON-PC.wav | Optimus, rapport système | Optimus Rapport Système | 0 |
| ggml-small | utt06-R3CON-PC.wav | Optimus, passe en mode combat | Optimus passe en mode combat. | 0 |
| ggml-small | utt07-R3CON-PC.wav | Optimus, allume les moteurs | Optimus, allume les moteurs ! | 0 |
| ggml-small | utt08-R3CON-PC.wav | Optimus, tu penses qu'on devrait se poser ? | Optimus, tu penses qu'on devrait se poser ? | 0 |

## Détail

| Modèle | Fichier | Passage | Audio (ms) | Chargement (ms) | Inférence (ms) | RTF |
|---|---|---|---|---|---|---|
| ggml-base | utt01-R3CON-PC.wav | 1 | 2100 | 1315.4 | 4744.1 | 2.259 |
| ggml-base | utt01-R3CON-PC.wav | 2 | 2100 | 410.2 | 4402.7 | 2.097 |
| ggml-base | utt01-R3CON-PC.wav | 3 | 2100 | 466 | 5096.2 | 2.427 |
| ggml-base | utt02-R3CON-PC.wav | 1 | 2300 | 554.8 | 5268.2 | 2.291 |
| ggml-base | utt02-R3CON-PC.wav | 2 | 2300 | 460.8 | 4597.9 | 1.999 |
| ggml-base | utt02-R3CON-PC.wav | 3 | 2300 | 489.7 | 4358.3 | 1.895 |
| ggml-base | utt03-R3CON-PC.wav | 1 | 2600 | 434.2 | 4739.6 | 1.823 |
| ggml-base | utt03-R3CON-PC.wav | 2 | 2600 | 490.8 | 4640.7 | 1.785 |
| ggml-base | utt03-R3CON-PC.wav | 3 | 2600 | 444.9 | 5237.2 | 2.014 |
| ggml-base | utt04-R3CON-PC.wav | 1 | 1900 | 308.7 | 4827.8 | 2.541 |
| ggml-base | utt04-R3CON-PC.wav | 2 | 1900 | 487.7 | 6058.1 | 3.188 |
| ggml-base | utt04-R3CON-PC.wav | 3 | 1900 | 541.4 | 4941 | 2.601 |
| ggml-base | utt05-R3CON-PC.wav | 1 | 2100 | 519.4 | 5896.1 | 2.808 |
| ggml-base | utt05-R3CON-PC.wav | 2 | 2100 | 703.1 | 5425.7 | 2.584 |
| ggml-base | utt05-R3CON-PC.wav | 3 | 2100 | 552 | 5365.8 | 2.555 |
| ggml-base | utt06-R3CON-PC.wav | 1 | 2200 | 848.9 | 5518.3 | 2.508 |
| ggml-base | utt06-R3CON-PC.wav | 2 | 2200 | 432.4 | 4660.4 | 2.118 |
| ggml-base | utt06-R3CON-PC.wav | 3 | 2200 | 1053.9 | 5912.6 | 2.688 |
| ggml-base | utt07-R3CON-PC.wav | 1 | 1700 | 738 | 5515.6 | 3.244 |
| ggml-base | utt07-R3CON-PC.wav | 2 | 1700 | 531.6 | 4454.7 | 2.62 |
| ggml-base | utt07-R3CON-PC.wav | 3 | 1700 | 622.9 | 4492.4 | 2.643 |
| ggml-base | utt08-R3CON-PC.wav | 1 | 2000 | 703.4 | 5672.3 | 2.836 |
| ggml-base | utt08-R3CON-PC.wav | 2 | 2000 | 704.8 | 5166 | 2.583 |
| ggml-base | utt08-R3CON-PC.wav | 3 | 2000 | 529.2 | 5531.9 | 2.766 |
| ggml-small | utt01-R3CON-PC.wav | 1 | 2100 | 1537.7 | 17553.7 | 8.359 |
| ggml-small | utt01-R3CON-PC.wav | 2 | 2100 | 1713.8 | 16880.1 | 8.038 |
| ggml-small | utt01-R3CON-PC.wav | 3 | 2100 | 1522.5 | 17264.6 | 8.221 |
| ggml-small | utt02-R3CON-PC.wav | 1 | 2300 | 1374.7 | 18366.8 | 7.986 |
| ggml-small | utt02-R3CON-PC.wav | 2 | 2300 | 2166.3 | 26050.7 | 11.326 |
| ggml-small | utt02-R3CON-PC.wav | 3 | 2300 | 2843 | 17497.3 | 7.608 |
| ggml-small | utt03-R3CON-PC.wav | 1 | 2600 | 1211 | 16550.5 | 6.366 |
| ggml-small | utt03-R3CON-PC.wav | 2 | 2600 | 1412.8 | 16880.5 | 6.492 |
| ggml-small | utt03-R3CON-PC.wav | 3 | 2600 | 1138.1 | 16085.5 | 6.187 |
| ggml-small | utt04-R3CON-PC.wav | 1 | 1900 | 1394.9 | 17910.8 | 9.427 |
| ggml-small | utt04-R3CON-PC.wav | 2 | 1900 | 1436.2 | 14751.8 | 7.764 |
| ggml-small | utt04-R3CON-PC.wav | 3 | 1900 | 1553.4 | 14650.9 | 7.711 |
| ggml-small | utt05-R3CON-PC.wav | 1 | 2100 | 1209.7 | 14890.4 | 7.091 |
| ggml-small | utt05-R3CON-PC.wav | 2 | 2100 | 1608.2 | 16128 | 7.68 |
| ggml-small | utt05-R3CON-PC.wav | 3 | 2100 | 1588.8 | 15007.9 | 7.147 |
| ggml-small | utt06-R3CON-PC.wav | 1 | 2200 | 1287.9 | 17724.6 | 8.057 |
| ggml-small | utt06-R3CON-PC.wav | 2 | 2200 | 1499.3 | 19890.1 | 9.041 |
| ggml-small | utt06-R3CON-PC.wav | 3 | 2200 | 1621.7 | 17454.6 | 7.934 |
| ggml-small | utt07-R3CON-PC.wav | 1 | 1700 | 1689.7 | 17199.2 | 10.117 |
| ggml-small | utt07-R3CON-PC.wav | 2 | 1700 | 1357 | 16959 | 9.976 |
| ggml-small | utt07-R3CON-PC.wav | 3 | 1700 | 1379.1 | 18902.6 | 11.119 |
| ggml-small | utt08-R3CON-PC.wav | 1 | 2000 | 1489.5 | 18513.5 | 9.257 |
| ggml-small | utt08-R3CON-PC.wav | 2 | 2000 | 1437.5 | 19189.1 | 9.595 |
| ggml-small | utt08-R3CON-PC.wav | 3 | 2000 | 1749 | 18826.4 | 9.413 |
