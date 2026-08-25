# Spike S0-2 - transcription Whisper

*R3CON-PC - 2026-08-24 19:23:43*

Binaire : `D:\app\80-Star Citizen\Optimus\whisper\whisper-bin-x64\Release\whisper-cli.exe`

## Contexte

| | |
|---|---|
| Processeur | AMD Ryzen 5 3600 6-Core Processor |
| Coeurs | 6 physiques / 12 logiques |
| MemoireGo | 31.9 |
| Threads | 4, 6, 8 |
| StarCitizen pendant la mesure | non |

Whisper encode toujours une fenêtre de **30 secondes**, quelle que soit la durée réelle
de l énoncé : un « ouvre les portes » de 2 s coûte autant qu une phrase de 25 s. Le RTF
rapporté à la durée du clip est donc trompeur - c est le temps ABSOLU par énoncé qui
compte. Bonne nouvelle au passage : les phrases longues ne coûtent pas plus cher.

## Synthèse

| Modèle | Threads | Mesures | Chargement (ms) | Inférence p50 (ms) | Inférence p95 (ms) | RTF moyen | WER moyen (%) | Cible p95 <= 500 ms |
|---|---|---|---|---|---|---|---|---|
| ggml-tiny | 4 | 16 | 141 | 991.3 | 1311.4 | 0.448 | 59.4 | non |
| ggml-tiny | 6 | 16 | 106 | 618.9 | 760.3 | 0.299 | 59.4 | non |
| ggml-tiny | 8 | 16 | 112 | 615.3 | 819.7 | 0.289 | 59.4 | non |
| ggml-base | 4 | 16 | 172 | 1492.3 | 1616.8 | 0.707 | 9.8 | non |
| ggml-base | 6 | 16 | 164 | 1118.6 | 1212.1 | 0.543 | 9.8 | non |
| ggml-base | 8 | 16 | 164 | 1024.5 | 1123.1 | 0.488 | 9.8 | non |
| ggml-small | 4 | 16 | 466 | 4957.6 | 5212.4 | 2.388 | 10.4 | non |
| ggml-small | 6 | 16 | 461 | 3993.4 | 4264.5 | 1.935 | 10.4 | non |
| ggml-small | 8 | 16 | 466 | 3520.2 | 3838 | 1.706 | 10.4 | non |

Le chargement est payé une seule fois au démarrage d Optimus ; seule l inférence entre
dans le budget de latence de docs/09.

## Transcriptions

| Modèle | Fichier | Attendu | Transcription | WER (%) |
|---|---|---|---|---|
| ggml-tiny | utt01-R3CON-PC.wav | Optimus, ouvre les portes | Optimus ouvre les portes. | 0 |
| ggml-tiny | utt02-R3CON-PC.wav | Optimus, prépare le saut quantique | Optimus prépare le soquantique. | 40 |
| ggml-tiny | utt03-R3CON-PC.wav | Optimus, mets les boucliers sur l'avant | Optimus mais les bouquiers sur l'avant. | 33.3 |
| ggml-tiny | utt04-R3CON-PC.wav | Optimus, active le scan | Optimus active le scan | 0 |
| ggml-tiny | utt05-R3CON-PC.wav | Optimus, rapport système | Au-dessus mues, rapport au système. | 133.3 |
| ggml-tiny | utt06-R3CON-PC.wav | Optimus, passe en mode combat | Au début, c'est pas ça mot de combat ! | 140 |
| ggml-tiny | utt07-R3CON-PC.wav | Optimus, allume les moteurs | Optimise à l'humil moteur. | 100 |
| ggml-tiny | utt08-R3CON-PC.wav | Optimus, tu penses qu'on devrait se poser ? | Où t'imus, tu penses qu'on devrait se poser ? | 28.6 |
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

| Modèle | Threads | Fichier | Passage | Audio (ms) | Chargement (ms) | Inférence (ms) | RTF |
|---|---|---|---|---|---|---|---|
| ggml-tiny | 4 | utt01-R3CON-PC.wav | 1 | 2100 | 162.1 | 994 | 0.473 |
| ggml-tiny | 4 | utt01-R3CON-PC.wav | 2 | 2100 | 177.2 | 1021.3 | 0.486 |
| ggml-tiny | 4 | utt02-R3CON-PC.wav | 1 | 2300 | 149.3 | 1057.2 | 0.46 |
| ggml-tiny | 4 | utt02-R3CON-PC.wav | 2 | 2300 | 165.4 | 1000.4 | 0.435 |
| ggml-tiny | 4 | utt03-R3CON-PC.wav | 1 | 2600 | 134.6 | 990.4 | 0.381 |
| ggml-tiny | 4 | utt03-R3CON-PC.wav | 2 | 2600 | 130 | 991.3 | 0.381 |
| ggml-tiny | 4 | utt04-R3CON-PC.wav | 1 | 1900 | 160.9 | 902.8 | 0.475 |
| ggml-tiny | 4 | utt04-R3CON-PC.wav | 2 | 1900 | 151.3 | 1019.4 | 0.537 |
| ggml-tiny | 4 | utt05-R3CON-PC.wav | 1 | 2100 | 161.8 | 1311.4 | 0.624 |
| ggml-tiny | 4 | utt05-R3CON-PC.wav | 2 | 2100 | 142.4 | 1213.1 | 0.578 |
| ggml-tiny | 4 | utt06-R3CON-PC.wav | 1 | 2200 | 143.2 | 880.4 | 0.4 |
| ggml-tiny | 4 | utt06-R3CON-PC.wav | 2 | 2200 | 157.3 | 801.8 | 0.364 |
| ggml-tiny | 4 | utt07-R3CON-PC.wav | 1 | 1700 | 100.7 | 682.3 | 0.401 |
| ggml-tiny | 4 | utt07-R3CON-PC.wav | 2 | 1700 | 100.4 | 682.8 | 0.402 |
| ggml-tiny | 4 | utt08-R3CON-PC.wav | 1 | 2000 | 107.2 | 785.1 | 0.393 |
| ggml-tiny | 4 | utt08-R3CON-PC.wav | 2 | 2000 | 113.3 | 761 | 0.38 |
| ggml-tiny | 6 | utt01-R3CON-PC.wav | 1 | 2100 | 106 | 554.9 | 0.264 |
| ggml-tiny | 6 | utt01-R3CON-PC.wav | 2 | 2100 | 102.6 | 562.2 | 0.268 |
| ggml-tiny | 6 | utt02-R3CON-PC.wav | 1 | 2300 | 113.1 | 633.6 | 0.275 |
| ggml-tiny | 6 | utt02-R3CON-PC.wav | 2 | 2300 | 101 | 590.7 | 0.257 |
| ggml-tiny | 6 | utt03-R3CON-PC.wav | 1 | 2600 | 101.4 | 626.8 | 0.241 |
| ggml-tiny | 6 | utt03-R3CON-PC.wav | 2 | 2600 | 103.4 | 639.2 | 0.246 |
| ggml-tiny | 6 | utt04-R3CON-PC.wav | 1 | 1900 | 102.8 | 557.8 | 0.294 |
| ggml-tiny | 6 | utt04-R3CON-PC.wav | 2 | 1900 | 108.2 | 549.9 | 0.289 |
| ggml-tiny | 6 | utt05-R3CON-PC.wav | 1 | 2100 | 101.5 | 760.3 | 0.362 |
| ggml-tiny | 6 | utt05-R3CON-PC.wav | 2 | 2100 | 101 | 751.8 | 0.358 |
| ggml-tiny | 6 | utt06-R3CON-PC.wav | 1 | 2200 | 104.5 | 614.2 | 0.279 |
| ggml-tiny | 6 | utt06-R3CON-PC.wav | 2 | 2200 | 103.1 | 618.9 | 0.281 |
| ggml-tiny | 6 | utt07-R3CON-PC.wav | 1 | 1700 | 101.9 | 599.8 | 0.353 |
| ggml-tiny | 6 | utt07-R3CON-PC.wav | 2 | 1700 | 114.7 | 612.4 | 0.36 |
| ggml-tiny | 6 | utt08-R3CON-PC.wav | 1 | 2000 | 103.7 | 646.9 | 0.323 |
| ggml-tiny | 6 | utt08-R3CON-PC.wav | 2 | 2000 | 127.4 | 678.8 | 0.339 |
| ggml-tiny | 8 | utt01-R3CON-PC.wav | 1 | 2100 | 105.2 | 527 | 0.251 |
| ggml-tiny | 8 | utt01-R3CON-PC.wav | 2 | 2100 | 105 | 546.8 | 0.26 |
| ggml-tiny | 8 | utt02-R3CON-PC.wav | 1 | 2300 | 106.7 | 662.4 | 0.288 |
| ggml-tiny | 8 | utt02-R3CON-PC.wav | 2 | 2300 | 105.3 | 560.2 | 0.244 |
| ggml-tiny | 8 | utt03-R3CON-PC.wav | 1 | 2600 | 101.8 | 620.4 | 0.239 |
| ggml-tiny | 8 | utt03-R3CON-PC.wav | 2 | 2600 | 162.8 | 616.1 | 0.237 |
| ggml-tiny | 8 | utt04-R3CON-PC.wav | 1 | 1900 | 116.8 | 507.9 | 0.267 |
| ggml-tiny | 8 | utt04-R3CON-PC.wav | 2 | 1900 | 109.8 | 511.9 | 0.269 |
| ggml-tiny | 8 | utt05-R3CON-PC.wav | 1 | 2100 | 121.9 | 819.7 | 0.39 |
| ggml-tiny | 8 | utt05-R3CON-PC.wav | 2 | 2100 | 108.1 | 811.1 | 0.386 |
| ggml-tiny | 8 | utt06-R3CON-PC.wav | 1 | 2200 | 109 | 617.2 | 0.281 |
| ggml-tiny | 8 | utt06-R3CON-PC.wav | 2 | 2200 | 103 | 551.5 | 0.251 |
| ggml-tiny | 8 | utt07-R3CON-PC.wav | 1 | 1700 | 102 | 526.5 | 0.31 |
| ggml-tiny | 8 | utt07-R3CON-PC.wav | 2 | 1700 | 111 | 533 | 0.314 |
| ggml-tiny | 8 | utt08-R3CON-PC.wav | 1 | 2000 | 111.5 | 615.3 | 0.308 |
| ggml-tiny | 8 | utt08-R3CON-PC.wav | 2 | 2000 | 113 | 651.3 | 0.326 |
| ggml-base | 4 | utt01-R3CON-PC.wav | 1 | 2100 | 170.8 | 1536.3 | 0.732 |
| ggml-base | 4 | utt01-R3CON-PC.wav | 2 | 2100 | 171.4 | 1571.4 | 0.748 |
| ggml-base | 4 | utt02-R3CON-PC.wav | 1 | 2300 | 173.6 | 1564.4 | 0.68 |
| ggml-base | 4 | utt02-R3CON-PC.wav | 2 | 2300 | 167.5 | 1504.3 | 0.654 |
| ggml-base | 4 | utt03-R3CON-PC.wav | 1 | 2600 | 170.1 | 1546.6 | 0.595 |
| ggml-base | 4 | utt03-R3CON-PC.wav | 2 | 2600 | 201.5 | 1616.8 | 0.622 |
| ggml-base | 4 | utt04-R3CON-PC.wav | 1 | 1900 | 173.2 | 1468.1 | 0.773 |
| ggml-base | 4 | utt04-R3CON-PC.wav | 2 | 1900 | 167 | 1430.5 | 0.753 |
| ggml-base | 4 | utt05-R3CON-PC.wav | 1 | 2100 | 172.9 | 1293.8 | 0.616 |
| ggml-base | 4 | utt05-R3CON-PC.wav | 2 | 2100 | 179.1 | 1328.6 | 0.633 |
| ggml-base | 4 | utt06-R3CON-PC.wav | 1 | 2200 | 160.6 | 1474.5 | 0.67 |
| ggml-base | 4 | utt06-R3CON-PC.wav | 2 | 2200 | 177.5 | 1577.3 | 0.717 |
| ggml-base | 4 | utt07-R3CON-PC.wav | 1 | 1700 | 170.3 | 1428.8 | 0.84 |
| ggml-base | 4 | utt07-R3CON-PC.wav | 2 | 1700 | 176.8 | 1364.8 | 0.803 |
| ggml-base | 4 | utt08-R3CON-PC.wav | 1 | 2000 | 160.2 | 1492.3 | 0.746 |
| ggml-base | 4 | utt08-R3CON-PC.wav | 2 | 2000 | 162.4 | 1446.7 | 0.723 |
| ggml-base | 6 | utt01-R3CON-PC.wav | 1 | 2100 | 164.8 | 1104 | 0.526 |
| ggml-base | 6 | utt01-R3CON-PC.wav | 2 | 2100 | 160.3 | 1172.1 | 0.558 |
| ggml-base | 6 | utt02-R3CON-PC.wav | 1 | 2300 | 167 | 1178.5 | 0.512 |
| ggml-base | 6 | utt02-R3CON-PC.wav | 2 | 2300 | 166.4 | 1131.7 | 0.492 |
| ggml-base | 6 | utt03-R3CON-PC.wav | 1 | 2600 | 162.9 | 1212.1 | 0.466 |
| ggml-base | 6 | utt03-R3CON-PC.wav | 2 | 2600 | 164.8 | 1192.6 | 0.459 |
| ggml-base | 6 | utt04-R3CON-PC.wav | 1 | 1900 | 165.5 | 1093.6 | 0.576 |
| ggml-base | 6 | utt04-R3CON-PC.wav | 2 | 1900 | 162.6 | 1084.4 | 0.571 |
| ggml-base | 6 | utt05-R3CON-PC.wav | 1 | 2100 | 160.7 | 1088.3 | 0.518 |
| ggml-base | 6 | utt05-R3CON-PC.wav | 2 | 2100 | 173.7 | 1090.6 | 0.519 |
| ggml-base | 6 | utt06-R3CON-PC.wav | 1 | 2200 | 160.4 | 1115.5 | 0.507 |
| ggml-base | 6 | utt06-R3CON-PC.wav | 2 | 2200 | 161 | 1117.9 | 0.508 |
| ggml-base | 6 | utt07-R3CON-PC.wav | 1 | 1700 | 158.8 | 1093.5 | 0.643 |
| ggml-base | 6 | utt07-R3CON-PC.wav | 2 | 1700 | 162.1 | 1118.6 | 0.658 |
| ggml-base | 6 | utt08-R3CON-PC.wav | 1 | 2000 | 166.1 | 1188.5 | 0.594 |
| ggml-base | 6 | utt08-R3CON-PC.wav | 2 | 2000 | 166.6 | 1173.6 | 0.587 |
| ggml-base | 8 | utt01-R3CON-PC.wav | 1 | 2100 | 171.5 | 975.5 | 0.465 |
| ggml-base | 8 | utt01-R3CON-PC.wav | 2 | 2100 | 160.7 | 972.1 | 0.463 |
| ggml-base | 8 | utt02-R3CON-PC.wav | 1 | 2300 | 162.2 | 1026.9 | 0.446 |
| ggml-base | 8 | utt02-R3CON-PC.wav | 2 | 2300 | 160 | 1024.5 | 0.445 |
| ggml-base | 8 | utt03-R3CON-PC.wav | 1 | 2600 | 160.4 | 1071.4 | 0.412 |
| ggml-base | 8 | utt03-R3CON-PC.wav | 2 | 2600 | 163.9 | 1078.7 | 0.415 |
| ggml-base | 8 | utt04-R3CON-PC.wav | 1 | 1900 | 163.3 | 953.9 | 0.502 |
| ggml-base | 8 | utt04-R3CON-PC.wav | 2 | 1900 | 161.8 | 957.6 | 0.504 |
| ggml-base | 8 | utt05-R3CON-PC.wav | 1 | 2100 | 161.8 | 957 | 0.456 |
| ggml-base | 8 | utt05-R3CON-PC.wav | 2 | 2100 | 165 | 1009 | 0.48 |
| ggml-base | 8 | utt06-R3CON-PC.wav | 1 | 2200 | 167.5 | 1072.4 | 0.487 |
| ggml-base | 8 | utt06-R3CON-PC.wav | 2 | 2200 | 168.4 | 998.3 | 0.454 |
| ggml-base | 8 | utt07-R3CON-PC.wav | 1 | 1700 | 163 | 1027.8 | 0.605 |
| ggml-base | 8 | utt07-R3CON-PC.wav | 2 | 1700 | 168.9 | 983.3 | 0.578 |
| ggml-base | 8 | utt08-R3CON-PC.wav | 1 | 2000 | 163 | 1123.1 | 0.562 |
| ggml-base | 8 | utt08-R3CON-PC.wav | 2 | 2000 | 163.7 | 1065.9 | 0.533 |
| ggml-small | 4 | utt01-R3CON-PC.wav | 1 | 2100 | 487.8 | 4942.4 | 2.354 |
| ggml-small | 4 | utt01-R3CON-PC.wav | 2 | 2100 | 455.6 | 4987.7 | 2.375 |
| ggml-small | 4 | utt02-R3CON-PC.wav | 1 | 2300 | 451.5 | 5060.4 | 2.2 |
| ggml-small | 4 | utt02-R3CON-PC.wav | 2 | 2300 | 484.4 | 5016.1 | 2.181 |
| ggml-small | 4 | utt03-R3CON-PC.wav | 1 | 2600 | 455.6 | 5212.4 | 2.005 |
| ggml-small | 4 | utt03-R3CON-PC.wav | 2 | 2600 | 454.4 | 5130 | 1.973 |
| ggml-small | 4 | utt04-R3CON-PC.wav | 1 | 1900 | 476.3 | 4872.1 | 2.564 |
| ggml-small | 4 | utt04-R3CON-PC.wav | 2 | 1900 | 456.9 | 4877.4 | 2.567 |
| ggml-small | 4 | utt05-R3CON-PC.wav | 1 | 2100 | 459.7 | 4803.6 | 2.287 |
| ggml-small | 4 | utt05-R3CON-PC.wav | 2 | 2100 | 456.3 | 4799.9 | 2.286 |
| ggml-small | 4 | utt06-R3CON-PC.wav | 1 | 2200 | 500.5 | 4917.6 | 2.235 |
| ggml-small | 4 | utt06-R3CON-PC.wav | 2 | 2200 | 457.2 | 4898.5 | 2.227 |
| ggml-small | 4 | utt07-R3CON-PC.wav | 1 | 1700 | 458.7 | 4957.6 | 2.916 |
| ggml-small | 4 | utt07-R3CON-PC.wav | 2 | 1700 | 467.6 | 4932.4 | 2.901 |
| ggml-small | 4 | utt08-R3CON-PC.wav | 1 | 2000 | 462.1 | 5169.7 | 2.585 |
| ggml-small | 4 | utt08-R3CON-PC.wav | 2 | 2000 | 474.9 | 5108.2 | 2.554 |
| ggml-small | 6 | utt01-R3CON-PC.wav | 1 | 2100 | 458.1 | 3903.1 | 1.859 |
| ggml-small | 6 | utt01-R3CON-PC.wav | 2 | 2100 | 461.6 | 3982 | 1.896 |
| ggml-small | 6 | utt02-R3CON-PC.wav | 1 | 2300 | 452.8 | 4044 | 1.758 |
| ggml-small | 6 | utt02-R3CON-PC.wav | 2 | 2300 | 454.1 | 4054.5 | 1.763 |
| ggml-small | 6 | utt03-R3CON-PC.wav | 1 | 2600 | 495.9 | 4220.8 | 1.623 |
| ggml-small | 6 | utt03-R3CON-PC.wav | 2 | 2600 | 458.5 | 4216.4 | 1.622 |
| ggml-small | 6 | utt04-R3CON-PC.wav | 1 | 1900 | 454.2 | 3977.7 | 2.094 |
| ggml-small | 6 | utt04-R3CON-PC.wav | 2 | 1900 | 458.1 | 3916 | 2.061 |
| ggml-small | 6 | utt05-R3CON-PC.wav | 1 | 2100 | 453.9 | 3915.3 | 1.864 |
| ggml-small | 6 | utt05-R3CON-PC.wav | 2 | 2100 | 459.7 | 3926.4 | 1.87 |
| ggml-small | 6 | utt06-R3CON-PC.wav | 1 | 2200 | 454.2 | 3941.7 | 1.792 |
| ggml-small | 6 | utt06-R3CON-PC.wav | 2 | 2200 | 455.5 | 3962.3 | 1.801 |
| ggml-small | 6 | utt07-R3CON-PC.wav | 1 | 1700 | 483.4 | 3993.4 | 2.349 |
| ggml-small | 6 | utt07-R3CON-PC.wav | 2 | 1700 | 455.4 | 4015.4 | 2.362 |
| ggml-small | 6 | utt08-R3CON-PC.wav | 1 | 2000 | 464 | 4264.5 | 2.132 |
| ggml-small | 6 | utt08-R3CON-PC.wav | 2 | 2000 | 462 | 4232.8 | 2.116 |
| ggml-small | 8 | utt01-R3CON-PC.wav | 1 | 2100 | 455.3 | 3463.7 | 1.649 |
| ggml-small | 8 | utt01-R3CON-PC.wav | 2 | 2100 | 471.2 | 3454.4 | 1.645 |
| ggml-small | 8 | utt02-R3CON-PC.wav | 1 | 2300 | 505.7 | 3567.3 | 1.551 |
| ggml-small | 8 | utt02-R3CON-PC.wav | 2 | 2300 | 466.2 | 3582.2 | 1.557 |
| ggml-small | 8 | utt03-R3CON-PC.wav | 1 | 2600 | 461.8 | 3728 | 1.434 |
| ggml-small | 8 | utt03-R3CON-PC.wav | 2 | 2600 | 461.1 | 3707.3 | 1.426 |
| ggml-small | 8 | utt04-R3CON-PC.wav | 1 | 1900 | 462.1 | 3455.5 | 1.819 |
| ggml-small | 8 | utt04-R3CON-PC.wav | 2 | 1900 | 475.4 | 3457.9 | 1.82 |
| ggml-small | 8 | utt05-R3CON-PC.wav | 1 | 2100 | 460.4 | 3446.5 | 1.641 |
| ggml-small | 8 | utt05-R3CON-PC.wav | 2 | 2100 | 486.7 | 3415.7 | 1.627 |
| ggml-small | 8 | utt06-R3CON-PC.wav | 1 | 2200 | 455.9 | 3563.1 | 1.62 |
| ggml-small | 8 | utt06-R3CON-PC.wav | 2 | 2200 | 453.2 | 3448.8 | 1.568 |
| ggml-small | 8 | utt07-R3CON-PC.wav | 1 | 1700 | 456.7 | 3509.1 | 2.064 |
| ggml-small | 8 | utt07-R3CON-PC.wav | 2 | 1700 | 460.6 | 3520.2 | 2.071 |
| ggml-small | 8 | utt08-R3CON-PC.wav | 1 | 2000 | 460.2 | 3838 | 1.919 |
| ggml-small | 8 | utt08-R3CON-PC.wav | 2 | 2000 | 466.8 | 3780.3 | 1.89 |
