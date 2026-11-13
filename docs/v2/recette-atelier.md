# JVTECH Atelier : recette sur PC réels (lot 1)

Les tests automatiques (CI) vérifient les règles : statut avant vente, passes Windows Update, conditions de
Sysprep, annonce, rapport. Ils ne prouvent pas que les actions marchent sur un vrai PC. Rien n'est « validé »
tant que la ligne correspondante n'est pas faite ici, avec la date, le PC et le résultat.

Toujours sur un **PC de test** (jamais un PC de client ni un poste de production), installé par clé USB.
Garder le dossier `Atelier\<V…>` de chaque essai : c'est la preuve.

| N° | Essai | Résultat attendu | Fait le / PC / résultat |
|---|---|---|---|
| R1 | Lancer `JvtechAtelier.exe` depuis la clé, en mode audit | Demande des droits administrateur, en-tête « Windows en mode audit » | |
| R2 | Même chose dans une session Windows normale | En-tête « session normale », étape Finalisation : « Pas en mode audit » | |
| R3 | Identification : corriger l'écran, saisir V…, enregistrer | Dossier `V…` sur la clé, `inventaire.ini` garde la valeur détectée ET la correction | |
| R4 | Relancer Atelier sur le même PC | Dossier retrouvé par le n° de série, corrections remises | |
| R5 | Préparation : passe Windows Update | Mises à jour installées, ligne « Action windows-update » dans `journal.txt` (avant, après, durée) | |
| R6 | « Redémarrer et reprendre » | Redémarrage dans 30 s, Atelier se relance seul après l'ouverture de session et fait la passe suivante | |
| R7 | Même essai R6 dans une session normale avec contrôle de compte | Noter si Windows bloque la relance : si oui, relancer Atelier à la main, la passe suivante continue | |
| R8 | Débrancher le réseau pendant une passe | Échec tracé « Recherche impossible », pas de boucle, statut « À vérifier » | |
| R9 | Pack de base 7-Zip + VLC (winget absent au départ en mode audit) | winget enregistré, les deux installés, « déjà installé » au 2e essai | |
| R10 | Un logiciel en option (Firefox) | Installé ; en cas d'échec, noté sans bloquer le pack de base | |
| R11 | Pilotes : PC avec un périphérique sans pilote | Liste des périphériques en erreur, réserve dans l'annonce | |
| R12 | Diagnostic mémoire de Windows | Fenêtre Windows ouverte ; après redémarrage, résultat relu dans « Contrôles et tests » | |
| R13 | Contrôles : SSD NVMe, HDD SATA, disque USB branché | Disques internes lus, USB ignoré ; « contrôle impossible » = À vérifier | |
| R14 | Batterie d'un portable usé | Nominale, actuelle, usure, cycles ; réserve selon les seuils de `atelier.ini` | |
| R15 | Tests manuels avec un échec en « réserve » | Réserve écrite dans l'annonce et le rapport, technicien et heure gardés | |
| R16 | Fiche, annonce et rapport | `fiche-interne.txt`, `annonce.txt`, `rapport.html` (s'ouvre dans le navigateur, imprimable en PDF) | |
| R17 | Changer un test après l'enregistrement de la fiche | Finalisation refusée : « l'enregistrer à nouveau » | |
| R18 | Finalisation avec statut Incomplet | Bouton désactivé, raisons listées | |
| R19 | Finalisation en mode audit, statut Prêt, n° V… tapé | Sysprep, extinction, au démarrage suivant : écran de premier démarrage (OOBE), pas de retour en mode audit | |
| R20 | Après R19 : vérifier le PC | Logiciels toujours là, aucune donnée effacée en dehors de ce que fait Sysprep, fichiers de réponse renommés en `.jvtech` | |
| R21 | Sysprep qui échoue (application du Store mise à jour pour le compte) | Échec affiché avec la fin de `setuperr.log`, PC pas éteint | |

Limites connues à vérifier pendant la recette :
- La relance automatique utilise RunOnce du compte : fiable en mode audit (compte Administrateur intégré) ;
  dans une session normale, Windows peut la bloquer (R7).
- La clé USB doit rester branchée, avec la même lettre, pendant les redémarrages.
- Les journaux d'erreur d'Atelier (plantage) suivent le dossier de PC Inspector, pas encore la clé USB.
