# Spike S0-7 — l'audio d'une reconnaissance rejetée est-il récupérable ?

*Sondage manuel du 2026-08-28 sur R3coN-portable (i5-12450H, 12 processeurs logiques).*

## La question

Elle décide de l'architecture de l'étage Whisper, et donc d'une promesse faite au pilote.

L'écran de réglages affiche aujourd'hui : *« une conversation ordinaire se trouve rejetée sans
même avoir été transcrite. Rien de ce que vous dites d'autre n'est analysé. »* C'est vrai tant que
la grammaire est fermée.

Si l'audio d'un rejet se récupère depuis le moteur Windows, Whisper se branche **derrière** lui :
seuls les énoncés qui ont déjà déclenché le moteur sont transcrits, et la promesse tient encore.
Sinon, il faut une seconde capture permanente — et la phrase ci-dessus devient fausse.

## Protocole

Matériel d'essai fabriqué par Piper (`fr_FR-tom-medium`), donc reproductible sans micro et sans
hasard. Grammaire fermée de la même forme que celle d'Optimus : mot d'éveil en tête, alternatives
finies (`allume les lumieres`, `eteins les lumieres`, `mode scan`).

Deux énoncés : un dans le catalogue, un hors catalogue.

## Résultat — l'audio est récupérable, dans les deux cas

| Énoncé | Verdict du moteur | Texte rendu | Confiance | Audio |
|---|---|---|---|---|
| « Optimus, allume les lumières » | `SpeechRecognized` | « Optimus allume les lumieres » | 0,825 | **récupéré**, 2,12 s |
| « Optimus, qu'est-ce que tu penses de ce vaisseau ? » | `SpeechRecognitionRejected` | **vide** | 0,000 | **récupéré**, 2,88 s |

`RecognitionResult.Audio` est présent sur les deux événements et s'écrit par
`WriteToWaveStream`. Format rendu : **16 000 Hz, 16 bits, 1 canal** — exactement ce que
`whisper.cpp` exige, sans rééchantillonnage.

### Deux conséquences

1. **L'étage Whisper n'a pas besoin d'ouvrir un micro.** Il se branche sur l'audio que le moteur
   rapide lui tend. La promesse de vie privée survit, à condition de le dire correctement :
   *« seuls les énoncés qui vous étaient adressés sont transcrits »*.

2. **Un rejet ne rend pas « la formulation la plus proche », il rend une chaîne vide.** La
   description de D45 était optimiste : aujourd'hui, la parole libre ne produit pas une mauvaise
   transcription, elle ne produit **rien du tout**. L'étage conversationnel (chantier 2) n'est
   donc pas « peu atteignable » à la voix — il est inatteignable.

## Latence — confirmation de S0-2 sur une autre machine

`whisper.cpp b4938`, `ggml-base`, sur l'audio récupéré ci-dessus.

| Réglage | Ce sondage (portable) | S0-2 (R3CON-PC) | WER mesuré en S0-2 |
|---|---|---|---|
| 8 fils, contexte complet | 905 – 929 ms | 969 ms (p50) | **9,8 %** |
| 8 fils, `-ac 768` | 536 – 561 ms | 578 ms (p50) | 14,8 % |
| 4 fils, contexte complet | 1 213 ms | — | — |

Le chargement du modèle coûte **167 ms**, payés une fois si le processus est persistant — même
patron que Piper (D55).

La propriété déjà trouvée en S0-2 se vérifie : **le coût d'encodage ne dépend pas de la durée de
l'énoncé.** 2,12 s et 2,88 s coûtent le même temps, parce que Whisper encode toujours une fenêtre
de 30 secondes.

### Ce que la transcription donne

| Énoncé | Transcription Whisper |
|---|---|
| hors catalogue (2,88 s) | « Optimus, qu'est-ce que tu penses de ce vaisseau ? » — **exacte** |
| dans le catalogue (2,12 s) | « Optimus, à une légumière. » — **fausse** |
| dans le catalogue, avec amorce de vocabulaire | « Optimus, aume les lumiere. » — approchée |

Ce n'est pas une anomalie mais l'expression du WER de 9,8 % mesuré en S0-2 : sur une commande de
trois mots, une erreur d'un mot sur dix suffit à casser l'énoncé. Le rapprochement flou d'Optimus
absorbe une partie de cet écart — « aume les lumiere » reste très proche de « allume les
lumieres » — mais pas tout.

**Réserve importante** : l'audio d'essai est **synthétique**. Whisper est entraîné sur de la voix
humaine, et la parole de synthèse le met en difficulté d'une façon qui ne préjuge pas de la vôtre.
Ces chiffres valent pour la latence ; ils ne valent **pas** comme verdict sur la reconnaissance de
commandes à la voix réelle. Cette mesure-là reste à faire, au micro.

## Ce que ça arbitre

| Mode | Coût sur les commandes connues | Ce que ça débloque |
|---|---|---|
| **Éteint** | aucun | rien — état actuel |
| **Sur les rejets** | **aucun** | la parole libre, qui ne produit rien aujourd'hui |
| **Sur tout** | **~900 ms par commande**, et un WER de 9,8 % là où la grammaire fermée rend 0,825 de confiance | un rapprochement flou qui travaille sur du vrai texte |

Le mode « sur tout » reste offert parce que le pilote doit pouvoir en juger sur sa propre voix et
sa propre machine — mais l'écran doit annoncer ce qu'il coûte, sans l'enjoliver.
