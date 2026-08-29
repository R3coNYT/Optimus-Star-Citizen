# Signature de code

*Dernier chantier de la feuille de route. Ouvert le 2026-08-29.*

## Le problème, en une phrase

L'installateur d'Optimus n'est pas signé. SmartScreen avertit donc chaque personne qui le
télécharge — « Windows a protégé votre ordinateur », un bouton *Ne pas exécuter* mis en avant et
*Informations complémentaires* caché — et Smart App Control, actif par défaut sur certaines
installations de Windows 11, le refuse purement et simplement. **Tant que cette signature
n'existe pas, la diffusion publique reste bloquée** (risque R16).

L'installateur n'a pas résolu ce problème, il l'a concentré : il y a désormais **un** fichier à
signer au lieu de vingt-quatre.

## Pourquoi SignPath, et pas un certificat acheté

Depuis juin 2023, le CA/Browser Forum impose que la clé privée d'un certificat de signature de
code vive dans un matériel certifié — jeton USB ou HSM infonuagique. Le prix a suivi : compter
**200 à 600 € par an**, ce que le pilote a explicitement écarté.

Un certificat auto-signé ne sert à rien : SmartScreen ne juge pas la présence d'une signature
mais la **réputation** de l'autorité qui l'a émise, et une autorité inconnue n'en a aucune.

Reste la **SignPath Foundation**, qui signe gratuitement pour les projets libres. Elle ne délivre
pas de certificat : elle signe elle-même, depuis son infrastructure, des artefacts qu'une chaîne
de compilation publique lui soumet. Personne — le mainteneur compris — ne détient jamais la clé.
C'est ce qui rend le service tenable pour elle, et c'est aussi ce qui dicte tout le reste de ce
document.

## Ce que la fondation exige

Relevé sur [signpath.org/terms.html](https://signpath.org/terms.html) le 2026-08-29.

| Condition | Où en est Optimus |
|---|---|
| Pas de logiciel malveillant | ✅ |
| **Licence libre approuvée par l'OSI**, sans double licence commerciale | ❌ **aucune licence** |
| Aucun composant propriétaire | ✅ Piper et whisper.cpp sont sous MIT, téléchargés et non embarqués |
| Projet activement maintenu | ✅ |
| **Base de code publiquement accessible** | ❌ **le dépôt est privé** |
| **Déjà publié sous la forme à signer** | ❌ aucune publication publique |
| Fonctionnement décrit sur la page de téléchargement | ⚠️ le README existe, il faudra une page de publication |
| L'équipe qui signe est celle qui développe, et possède le dépôt | ✅ |
| Ne signer que ce qu'on a compilé soi-même | ✅ |
| **Double authentification** pour tous les contributeurs | ⚠️ à vérifier sur le compte GitHub |
| Rôles définis : auteurs, relecteurs, approbateurs | ✅ voir plus bas |
| **Politique de signature publiée** sur la page d'accueil du projet | ✅ voir plus bas |
| Compilations automatisées et vérifiables | ✅ `.github/workflows/release.yml` |

**Trois manques, et ils tiennent tous à une seule décision** : rendre le dépôt public sous une
licence libre. C'est un choix du pilote, pas une étape technique — il engage ce qu'il pourra faire
du code ensuite, et il expose le code à qui veut le lire.

## La chaîne de compilation

`.github/workflows/release.yml` se déclenche sur un tag `vX.Y.Z` et enchaîne :

1. les tests — un installateur ne sort pas d'un code qui échoue ;
2. la publication de l'application ;
3. **la signature de l'exécutable** ;
4. la construction de l'installateur, qui contient donc un exécutable **déjà signé** ;
5. **la signature de l'installateur** ;
6. l'empreinte SHA-256 et la publication.

L'ordre des points 3 et 4 n'est pas négociable. Signer seulement l'installateur laisserait Smart
App Control refuser l'application une fois installée : c'est l'exécutable qu'il inspecte au
lancement, pas ce qui l'a posé.

Les deux étapes de signature se sautent d'elles-mêmes tant que la variable `SIGNPATH_PROJECT`
n'existe pas. **La chaîne fonctionne donc dès aujourd'hui**, et produit un installateur non signé,
exactement celui qu'on construit à la main — ce qui permet de l'éprouver avant que la fondation
ait répondu.

### Ce qu'il faudra renseigner, une fois le projet accepté

| | Où | Quoi |
|---|---|---|
| `SIGNPATH_API_TOKEN` | secret du dépôt | jeton d'API SignPath |
| `SIGNPATH_ORGANIZATION_ID` | variable du dépôt | identifiant de l'organisation |
| `SIGNPATH_PROJECT` | variable du dépôt | abrégé du projet |

Et côté SignPath, deux **configurations d'artefact** nommées `app` et `installateur`, décrivant
respectivement l'archive contenant `Optimus.App.exe` et l'installateur nu.

## Politique de signature de code

*Cette section répond à l'exigence de la fondation. Elle doit rester accessible publiquement.*

**Projet.** Optimus — copilote vocal pour Star Citizen.
**Dépôt.** https://github.com/R3coNYT/Optimus-Star-Citizen

**Rôles.**

| Rôle | Qui | Responsabilité |
|---|---|---|
| Auteur | R3coN | écrit et publie le code, déclenche les compilations |
| Relecteur | R3coN | relit les changements avant qu'ils entrent dans une version |
| Approbateur | R3coN | approuve chaque demande de signature dans SignPath |

*Projet à mainteneur unique : les trois rôles sont tenus par la même personne, ce que la
fondation admet. Le contrôle réel ne vient alors pas de la séparation des rôles mais de la
chaîne — seul un tag poussé sur le dépôt public peut déclencher une signature, et le contenu
signé est reconstructible par quiconque à partir du code.*

**Ce qui est signé.** L'exécutable `Optimus.App.exe` et l'installateur
`Optimus-X.Y.Z-installateur.exe`, tous deux produits par
[`.github/workflows/release.yml`](../.github/workflows/release.yml) sur un exécuteur GitHub, à
partir du code de ce dépôt et de lui seul.

**Ce qui n'est jamais signé.** Les composants tiers téléchargés à l'installation — le moteur
Piper, ses voix, whisper.cpp et son modèle — ne sont ni recompilés ni signés par ce projet. Ils
sont vérifiés par empreinte SHA-256 déclarée dans le script d'installation, et conservent la
signature que leurs auteurs leur ont donnée, ou son absence.

**Données personnelles.** Aucune. Optimus fonctionne entièrement en local et n'émet aucune
requête réseau de lui-même. Ni le projet ni la fondation ne collectent de données auprès des
personnes qui installent le logiciel.

**Contact.** Par les issues du dépôt.

## Ce qui reste à faire, dans l'ordre

1. **Décider** de la licence et rendre le dépôt public. *Pilote.*
2. Poser le fichier `LICENSE` et lier la politique ci-dessus depuis le README.
3. Vérifier que la double authentification est active sur le compte GitHub.
4. Publier une première version **non signée** par la chaîne, pour satisfaire « déjà publié sous
   la forme à signer » et éprouver le pipeline.
5. **Déposer la demande** sur signpath.org. *Pilote — je ne peux ni créer le compte ni signer la
   demande à sa place.* Délai annoncé : de quelques jours à quelques semaines.
6. Une fois acceptée : renseigner le secret et les deux variables, créer les configurations
   d'artefact, retagger.
7. Vérifier sur une machine tierce que SmartScreen se tait et que Smart App Control laisse
   passer.
