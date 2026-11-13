# JVTECH Audit : recette sur PC réels (lot 2)

Les tests automatiques (CI) vérifient les règles avec des données simulées (scénarios T01 à T11 du cahier) et que
le code d'Audit ne contient aucun appel qui modifie Windows. Ils ne prouvent pas ce que Windows renvoie sur un vrai PC.
Rien n'est « validé » tant que la ligne n'est pas faite ici, avec la date, le PC et le résultat.

Audit ne modifie rien, mais la recette se fait d'abord sur des **PC de l'atelier** (au moins 3, dont un portable et un
PC fixe), pas chez un client. Garder les rapports exportés : c'est la preuve.

| N° | Essai | Résultat attendu | Fait le / PC / résultat |
|---|---|---|---|
| T01 | Windows 10 Pro, processeur de la liste Microsoft | Windows hors support (date, ESU « à vérifier »), Windows 11 « Compatible » ou « Configuration à vérifier » avec la cause exacte, recommandation de passage | |
| T02 | Windows 10, processeur non pris en charge (ex. i5 de 4e à 7e génération) | Windows 11 « Incompatible (matériel) », raison « Processeur », recommandation de renouvellement à étudier | |
| T03 | TPM désactivé dans l'UEFI (ou absent) | Jamais « Compatible » : « réglage UEFI » si le processeur est listé, sinon « Impossible de conclure » | |
| T04 | Windows 11 avec un antivirus tiers (ESET, Bitdefender…) | Antivirus tiers nommé et « actif, à jour », ou « À vérifier » | |
| T05 | PC de domaine, Entra ID ou Intune | « Poste administré » ; pare-feu, UAC, bureau à distance gérés par l'entreprise : « À vérifier », pas « Anomalie » | |
| T06 | Un HDD sain, puis un SSD sain | Type exact (HDD/SSD, SATA/NVMe), capacité, « aucun indicateur de panne lu », jamais « sain garanti » ; HDD : remplacement par un SSD proposé | |
| T07 | Lancé sans droits (SMART souvent illisible), puis disque suspect | « santé non vérifiée » (À vérifier) ; disque en défaut : alerte critique en tête | |
| T08 | Portable avec batterie usée | Capacité nominale, actuelle, usure, cycles ; ou « non disponible » | |
| T09 | PC dont les emplacements mémoire sont mal annoncés | Pas d'emplacement libre inventé : « non déterminés » ou « selon le BIOS, à confirmer » | |
| T10 | PC sans réseau | Audit complet, réseau « Aucune connexion », « Accès Internet non testé » dans les limites | |
| T11 | Compte standard (sans droits administrateur) | Pas de demande d'élévation ; points non lus listés dans « Limites de cet audit » | |
| T12 | Windows en anglais | Mêmes résultats (lecture par identifiants, pas par textes traduits) | |
| T13 | Antivirus tiers et droits limités | Aucun contournement ; ce qui n'est pas lisible reste « À vérifier » | |
| T22 | Capture avant / après : export du registre (HKLM\SOFTWARE, HKLM\SYSTEM), liste des services, dossier %TEMP% | Aucune différence due à l'audit ; seule écriture : le rapport exporté à l'endroit choisi | |
| T25 | Rapport exporté (avec et sans anonymisation) | Ni mot de passe, ni clé BitLocker, ni secret Wi-Fi ; anonymisé : n° de série, nom du PC, domaine, comptes et IP masqués | |
| A1 | Durée | Tableau de bord affiché ; noter la durée (dépend du PC et des droits, aucune durée promise) | |
| A2 | Écran 1366×768 et mise à l'échelle 125–200 % | Cartes lisibles, défilement vertical seulement | |

Limites connues à vérifier pendant la recette :
- BitLocker sans droits administrateur : lu par l'Explorateur (propriété du lecteur). Si Windows ne la donne pas : « À vérifier ».
- Activation : lecture WMI parfois lente (quelques secondes) ; au-delà de 20 s, « non lue ».
- Dernière mise à jour : date du correctif le plus récent (Win32_QuickFixEngineering) ; à comparer avec l'historique de Windows Update.
- Fins de support : table relevée le 13/11/2026 ; une version plus récente que la table reste « à vérifier ».
- ESU de Windows 10 : non détectable de façon fiable, toujours signalée « à vérifier ».
