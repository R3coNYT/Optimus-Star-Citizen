# Spike S0-2 - transcription Whisper

*R3CON-PC - 2026-08-24 19:31:01*

Binaire : `D:\app\80-Star Citizen\Optimus\whisper\whisper-bin-x64\Release\whisper-cli.exe`

## Contexte

| | |
|---|---|
| Processeur | AMD Ryzen 5 3600 6-Core Processor |
| Coeurs | 6 physiques / 12 logiques |
| MemoireGo | 31.9 |
| Threads | 8, 12 |
| Contexte audio | complet (30 s), 768, 512 |
| StarCitizen pendant la mesure | non |

Whisper encode toujours une fenêtre de **30 secondes**, quelle que soit la durée réelle
de l énoncé : un « ouvre les portes » de 2 s coûte autant qu une phrase de 25 s. Le RTF
rapporté à la durée du clip est donc trompeur - c est le temps ABSOLU par énoncé qui
compte. Bonne nouvelle au passage : les phrases longues ne coûtent pas plus cher.

## Synthèse

| Modèle | Threads | Contexte audio | Mesures | Chargement (ms) | Inférence p50 (ms) | Inférence p95 (ms) | RTF moyen | WER moyen (%) | Cible p95 <= 500 ms |
|---|---|---|---|---|---|---|---|---|---|
| ggml-base | 8 | complet | 8 | 162 | 969.3 | 1054.5 | 0.472 | 9.8 | non |
| ggml-base | 8 | 768 | 8 | 162 | 578.4 | 632.2 | 0.271 | 14.8 | non |
| ggml-base | 8 | 512 | 8 | 162 | 439.8 | 485.2 | 0.205 | 31.1 | OUI |
| ggml-base | 12 | complet | 8 | 161 | 933.2 | 1025.7 | 0.454 | 9.8 | non |
| ggml-base | 12 | 768 | 8 | 162 | 552.5 | 631.3 | 0.264 | 14.8 | non |
| ggml-base | 12 | 512 | 8 | 163 | 424.1 | 500.5 | 0.206 | 31.1 | non |

Le chargement est payé une seule fois au démarrage d Optimus ; seule l inférence entre
dans le budget de latence de docs/09.

## Transcriptions

| Modèle | Fichier | Attendu | Transcription | WER (%) |
|---|---|---|---|---|
| ggml-base | utt01-R3CON-PC.wav | Optimus, ouvre les portes | Optimus ouvre les portes. | 0 |
| ggml-base | utt02-R3CON-PC.wav | Optimus, prépare le saut quantique | Optimus prépare le soquantique. | 40 |
| ggml-base | utt03-R3CON-PC.wav | Optimus, mets les boucliers sur l'avant | Optimus met les bouquillés sur l'avant. | 33.3 |
| ggml-base | utt04-R3CON-PC.wav | Optimus, active le scan | Optimus active le scan. | 0 |
| ggml-base | utt05-R3CON-PC.wav | Optimus, rapport système | Optimus Rapports Stem. | 66.7 |
| ggml-base | utt06-R3CON-PC.wav | Optimus, passe en mode combat | Optimus ne passe pas sur notre combat. | 80 |
| ggml-base | utt07-R3CON-PC.wav | Optimus, allume les moteurs | Optimus allume les moteurs. | 0 |
| ggml-base | utt08-R3CON-PC.wav | Optimus, tu penses qu'on devrait se poser ? | Optimus tu penses qu'on devrait esposer ? | 28.6 |

## Détail

| Modèle | Threads | Fichier | Passage | Audio (ms) | Chargement (ms) | Inférence (ms) | RTF |
|---|---|---|---|---|---|---|---|
| ggml-base | 8 | utt01-R3CON-PC.wav | 1 | 2100 | 160.3 | 955.4 | 0.455 |
| ggml-base | 8 | utt02-R3CON-PC.wav | 1 | 2300 | 171 | 992.8 | 0.432 |
| ggml-base | 8 | utt03-R3CON-PC.wav | 1 | 2600 | 162.1 | 1054.5 | 0.406 |
| ggml-base | 8 | utt04-R3CON-PC.wav | 1 | 1900 | 159.5 | 954.5 | 0.502 |
| ggml-base | 8 | utt05-R3CON-PC.wav | 1 | 2100 | 160 | 948.3 | 0.452 |
| ggml-base | 8 | utt06-R3CON-PC.wav | 1 | 2200 | 161.1 | 969.3 | 0.441 |
| ggml-base | 8 | utt07-R3CON-PC.wav | 1 | 1700 | 159.7 | 961.6 | 0.566 |
| ggml-base | 8 | utt08-R3CON-PC.wav | 1 | 2000 | 159.4 | 1045 | 0.522 |
| ggml-base | 8 | utt01-R3CON-PC.wav | 1 | 2100 | 169.2 | 543 | 0.259 |
| ggml-base | 8 | utt02-R3CON-PC.wav | 1 | 2300 | 159 | 578.4 | 0.251 |
| ggml-base | 8 | utt03-R3CON-PC.wav | 1 | 2600 | 159.5 | 605.8 | 0.233 |
| ggml-base | 8 | utt04-R3CON-PC.wav | 1 | 1900 | 163.2 | 519.7 | 0.274 |
| ggml-base | 8 | utt05-R3CON-PC.wav | 1 | 2100 | 159.6 | 632.2 | 0.301 |
| ggml-base | 8 | utt06-R3CON-PC.wav | 1 | 2200 | 161.6 | 535.1 | 0.243 |
| ggml-base | 8 | utt07-R3CON-PC.wav | 1 | 1700 | 160.8 | 527.5 | 0.31 |
| ggml-base | 8 | utt08-R3CON-PC.wav | 1 | 2000 | 164.2 | 595.6 | 0.298 |
| ggml-base | 8 | utt01-R3CON-PC.wav | 1 | 2100 | 160.4 | 399.3 | 0.19 |
| ggml-base | 8 | utt02-R3CON-PC.wav | 1 | 2300 | 159.3 | 439.8 | 0.191 |
| ggml-base | 8 | utt03-R3CON-PC.wav | 1 | 2600 | 165.6 | 473.3 | 0.182 |
| ggml-base | 8 | utt04-R3CON-PC.wav | 1 | 1900 | 162.6 | 374 | 0.197 |
| ggml-base | 8 | utt05-R3CON-PC.wav | 1 | 2100 | 160.8 | 485.2 | 0.231 |
| ggml-base | 8 | utt06-R3CON-PC.wav | 1 | 2200 | 159.9 | 412.3 | 0.187 |
| ggml-base | 8 | utt07-R3CON-PC.wav | 1 | 1700 | 158.9 | 392.5 | 0.231 |
| ggml-base | 8 | utt08-R3CON-PC.wav | 1 | 2000 | 165.9 | 456.7 | 0.228 |
| ggml-base | 12 | utt01-R3CON-PC.wav | 1 | 2100 | 159 | 925.1 | 0.441 |
| ggml-base | 12 | utt02-R3CON-PC.wav | 1 | 2300 | 158.8 | 957.2 | 0.416 |
| ggml-base | 12 | utt03-R3CON-PC.wav | 1 | 2600 | 160.7 | 1025.7 | 0.395 |
| ggml-base | 12 | utt04-R3CON-PC.wav | 1 | 1900 | 162.4 | 902.3 | 0.475 |
| ggml-base | 12 | utt05-R3CON-PC.wav | 1 | 2100 | 159.8 | 899.2 | 0.428 |
| ggml-base | 12 | utt06-R3CON-PC.wav | 1 | 2200 | 159.1 | 933.2 | 0.424 |
| ggml-base | 12 | utt07-R3CON-PC.wav | 1 | 1700 | 167.8 | 930.5 | 0.547 |
| ggml-base | 12 | utt08-R3CON-PC.wav | 1 | 2000 | 158.9 | 1009.4 | 0.505 |
| ggml-base | 12 | utt01-R3CON-PC.wav | 1 | 2100 | 159.6 | 527.5 | 0.251 |
| ggml-base | 12 | utt02-R3CON-PC.wav | 1 | 2300 | 161.9 | 552.5 | 0.24 |
| ggml-base | 12 | utt03-R3CON-PC.wav | 1 | 2600 | 165.7 | 576.8 | 0.222 |
| ggml-base | 12 | utt04-R3CON-PC.wav | 1 | 1900 | 169.1 | 498.4 | 0.262 |
| ggml-base | 12 | utt05-R3CON-PC.wav | 1 | 2100 | 160.3 | 631.3 | 0.301 |
| ggml-base | 12 | utt06-R3CON-PC.wav | 1 | 2200 | 160.5 | 514.5 | 0.234 |
| ggml-base | 12 | utt07-R3CON-PC.wav | 1 | 1700 | 162.8 | 513.2 | 0.302 |
| ggml-base | 12 | utt08-R3CON-PC.wav | 1 | 2000 | 158.6 | 593.7 | 0.297 |
| ggml-base | 12 | utt01-R3CON-PC.wav | 1 | 2100 | 159.8 | 407.4 | 0.194 |
| ggml-base | 12 | utt02-R3CON-PC.wav | 1 | 2300 | 170.8 | 424.1 | 0.184 |
| ggml-base | 12 | utt03-R3CON-PC.wav | 1 | 2600 | 162.2 | 452.9 | 0.174 |
| ggml-base | 12 | utt04-R3CON-PC.wav | 1 | 1900 | 160.4 | 384.5 | 0.202 |
| ggml-base | 12 | utt05-R3CON-PC.wav | 1 | 2100 | 160 | 500.5 | 0.238 |
| ggml-base | 12 | utt06-R3CON-PC.wav | 1 | 2200 | 165.5 | 402.7 | 0.183 |
| ggml-base | 12 | utt07-R3CON-PC.wav | 1 | 1700 | 161.7 | 394.4 | 0.232 |
| ggml-base | 12 | utt08-R3CON-PC.wav | 1 | 2000 | 165.6 | 481.4 | 0.241 |
