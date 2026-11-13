# Écritures sur le PC, données personnelles et conservation

Réponse aux points M01, M02 et M03 de l'audit du 13/11/2026.

## 1. « Lecture seule » : ce que cela veut dire exactement (M01)

PC Inspector et PC Sécurité garantissent **aucune modification de configuration** : ni registre, ni service, ni
tâche planifiée, ni pare-feu, ni compte, ni pilote. Ils font en revanche des **écritures temporaires ou de
résultats**, listées ici sans exception connue :

| Logiciel | Quand | Écriture | Effacée ? |
|---|---|---|---|
| PC Inspector | Test bureautique (au lancement) ou gaming (sur clic) | `winsat formal` : résultats dans `C:\Windows\Performance\WinSAT\DataStore` (comme le fait Windows lui-même) | Non (fichiers de Windows, quelques Ko) |
| PC Inspector | Test disque | 1 Go dans `%TEMP%\JVTECH-test-disque.tmp`, **seulement si au moins 5 Go sont libres** (sinon non mesuré, rien n'est écrit) | Oui, à la fin du test |
| PC Inspector | Test processeur | `7zr.exe` extrait dans le dossier de travail (`%TEMP%\JVTECH` ou `Temp` de la clé) | Non (dossier temporaire, quelques centaines de Ko) |
| PC Sécurité | Mots de passe enregistrés | Copie de la base « Login Data » du navigateur dans `%TEMP%\jvtech-logins-*.db` (le navigateur ouvert la verrouille) | Oui, juste après le comptage ; **et** au lancement de l'analyse suivante si l'analyse avait été interrompue |
| PC Sécurité | Réseaux Wi-Fi | Export `netsh` **sans** les mots de passe dans `%TEMP%\PcSecurite-wifi-*` | Idem |
| Les deux | Toujours | Journal du jour (5 Mo au plus), rapports d'erreur | Après 90 jours |
| Les deux | Export demandé | Rapport, fiche, étiquette, PDF là où le technicien choisit | Non : c'est le livrable |

Un SSD fragile : les écritures du test disque (1 Go) sont les seules notables. Le technicien qui veut **zéro
écriture notable** ferme PC Inspector avant la fin du bilan initial ou utilise PC Sécurité seul ; un mode
« strict » séparé n'a pas été ajouté (une option de plus à expliquer, sans gain démontré tant que le garde-fou
d'espace libre existe). À réexaminer si un cas réel le justifie.

## 2. Copie de la base des navigateurs (M02)

- Copie dans le dossier temporaire **de l'utilisateur** (`%TEMP%`, même disque que le profil, mêmes droits : seul
  l'utilisateur et les administrateurs y accèdent). Jamais sur une clé USB ni un disque externe.
- La base copiée est ouverte en lecture seule ; seule une requête `COUNT(*)` est faite. Aucun mot de passe n'est
  lu, déchiffré ni affiché ; seuls des nombres apparaissent dans le rapport.
- Purge : en fin de contrôle (bloc `finally`), et à chaque nouvelle analyse pour rattraper une analyse interrompue
  (`SecurityChecks.PurgeLeftovers`, test `LeftoverPurgeTests`).
- Mode sans copie : non proposé. Ouvrir directement la base du navigateur risquerait de gêner le navigateur ouvert
  ou d'abîmer sa base ; la copie est le choix le moins risqué pour le client.

## 3. Données personnelles et conservation (M03)

| Donnée | Où | Contenu personnel | Conservation |
|---|---|---|---|
| Journaux des logiciels | Dossier JVTECH (ou clé USB) | Nom du PC, chemins, noms de logiciels. Mots de passe, jetons et secrets **masqués** automatiquement | 90 jours, purge au lancement |
| « Journaux pour le support » | .zip créé sur clic dans le lanceur | Les 14 derniers jours des journaux ci-dessus | Le technicien décide ; à envoyer seulement à qui en a besoin |
| Rapports clients (HTML, TXT, PDF, fiche, étiquette) | Là où le technicien les enregistre (`Rapports\Client\Date` par défaut) | Nom du client si saisi, nom du PC, utilisateur Windows, numéro de série, logiciels | **Pas de purge automatique** : ce sont des documents remis au client et la trace de l'intervention |
| Sauvegarde PC Migration | Disque externe du client | Les fichiers du client ; Wi-Fi et mots de passe chiffrés (`.jvenc`) | À effacer par le technicien après la restauration, avec l'accord du client |
| Suivi PXE | Serveur de l'atelier | Numéro de série, modèle, état de préparation | Jusqu'à ce que le technicien les efface |

Règles à appliquer à l'atelier (RGPD) :

1. Ne saisir le nom du client que si le rapport lui est remis ou sert de suivi d'intervention.
2. Durée de conservation conseillée des rapports : **la durée de la garantie de l'intervention, puis 3 ans au
   plus** (preuve en cas de litige), puis suppression du dossier `Rapports\Client`.
3. Ne jamais transmettre un rapport ou un .zip de journaux à un tiers (audit, support) sans avoir retiré les
   rapports clients : les archives d'audit JVTECH ne contiennent aucun rapport client.
4. Effacer la sauvegarde PC Migration du disque externe dès que le client a confirmé la restauration.

Non fait, volontairement : anonymisation automatique des rapports (le rapport sert justement à identifier le PC
du client) et durée configurable dans les logiciels (la purge des rapports reste une décision humaine).
