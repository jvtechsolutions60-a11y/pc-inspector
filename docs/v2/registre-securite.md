# Registre des corrections de sécurité (audit externe v1.0.94)

Tenu à jour à chaque version. Une ligne ne passe à **Validé** qu'avec une preuve d'essai réel : date, machine
(VM ou PC de test, jamais un poste client ou de production), scénario du protocole (`docs/protocole-validation.md`),
résultat observé et capture ou journal joint. **Une compilation ou un test automatisé réussi ne suffit pas**, en
particulier pour les opérations sensibles (effacement, Sysprep, registre, restauration de données).

## Statuts

| Statut | Sens |
|---|---|
| Ouvert | Constat confirmé, pas encore corrigé |
| Corrigé (code) | Correction écrite, vérifiée par lecture du code seulement |
| Corrigé (tests auto) | Correction écrite et couverte par un test automatisé (données simulées) |
| Partiel | Risque réduit mais pas supprimé ; limite expliquée |
| Validé | Essai réel réussi, preuve archivée (voir colonne « Essai réel ») |
| Hors v2 | Logiciel non repris en v2 (PXE, Migration, Maintenance) : suivi gardé, mêmes règles |

Au 13/11/2026 (v1.0.105) : **aucune ligne n'est « Validé »**. Aucun essai réel n'a encore été fait.

## Registre

| Point | Logiciel | Correction (résumé) | Statut | Preuve automatisée | Essai réel (scénario) | Suite en v2 |
|---|---|---|---|---|---|---|
| C01 | Serveur PXE | Un seul disque exigé, EFFACER tapé sur le poste, sinon arrêt | Corrigé (tests auto) | `PxeScriptTests` | 2.1, 2.1b, 2.1c, 2.1d : à faire | Hors v2 |
| C02 | PC Maintenance → Atelier | Mode audit reconnu seulement avec `ImageState` connu | Corrigé (tests auto) | `PreparationTests` | 1.8 : à faire | Repris dans Atelier : Sysprep seulement en mode audit |
| C03 | PC Migration | Restauration sans écrasement, doublons à part | Corrigé (tests auto) | `RobocopyTests` | 3.11, 3.12 : à faire | Hors v2 |
| C04 | PC Migration | Navigateur non restauré si le déplacement échoue | Corrigé (code) | — | 3.13 : à faire | Hors v2 |
| I01 | PC Maintenance → Atelier | Plan d'alimentation copié puis remis | Corrigé (code) | — | 1.9, 5.7 : à faire | Repris dans Atelier |
| I02 | PC Maintenance → Atelier | Stratégies relevées une fois, remises à l'identique | Corrigé (tests auto) | `PolicyBackupTests` | 1.10 : à faire | Repris dans Atelier |
| I03 | PC Maintenance → Atelier | Seul le Sysprep officiel du mode audit est fermé | Corrigé (tests auto) | `SysprepWindowTests` | 5.8 : à faire | Repris dans Atelier |
| I04 | PC Maintenance | Nettoyage : documents mis de côté, jamais supprimés | Corrigé (tests auto) | `CopyCheckTests` | 5.9 : à faire | Hors v2 (pas de nettoyage dans Atelier) |
| I05 | PC Migration | Copie vérifiée (tailles + SHA-256 par échantillon) | Corrigé (tests auto) | `CopyCheckTests` | 3.14 : à faire | Hors v2 |
| I06 | PC Migration | Droits du dossier de secrets relus avant déchiffrement | Corrigé (tests auto) | `SecretsFolderTests` | — (scénario à écrire) | Hors v2 |
| I07 | PC Migration | Avertissement : effacement non garanti sur SSD | Partiel (limite physique) | — | — | Hors v2 |
| I08 | Serveur PXE | Compte `jvtechpxe` actif seulement en préparation | Partiel (identifiants dans l'image) | — | 2.10 : à faire | Hors v2 |
| I09 | PC Maintenance | Win11Debloat figé par commit + empreinte contrôlée | Corrigé (tests auto) | `Win11DebloatHashTests`, CI | 5.10 : à faire | Hors v2 (pas de debloat dans Atelier) |
| I10 | Tous | Attestation de provenance signée ; Authenticode à décider (D4) | Partiel | CI (`attest-build-provenance`) | 7.4 : à faire à la 1re publication | Appliqué aux 2 nouveaux .exe |
| I11 | Serveur PXE | Chaque PC inconnu autorisé sur le serveur | Corrigé (code) | — | 2.9 : à faire | Hors v2 |
| I12 | Tous | Programmes enfants dans un job Windows, arrêtés au délai | Corrigé (code) | — | 5.11 : à faire | Repris dans le socle commun |
| I13 | Tous | Lecture de sortie par file bornée, fil dédié | Corrigé (code) | — | 5.11 : à faire | Repris dans le socle commun |
| I14 | Serveur PXE | Une mise à jour d'image à la fois | Corrigé (code) | — | — (scénario à écrire) | Hors v2 |
| I15 | Serveur PXE | HTTPS, hôtes Microsoft, pas de version plus ancienne | Corrigé (tests auto) | `ImageUpdaterTests` | 2.11 : à faire | Hors v2 |
| I16 | Serveur PXE | Secrets illisibles détectés, aucun PC accepté | Partiel (lisibles par un admin) | — | 2.12 : à faire | Hors v2 |
| I17 | PC Migration | Rapport limité + sortie complète de robocopy gardée | Corrigé (code) | — | 3.15 : à faire | Hors v2 |
| I18 | PC Migration | Avertissement : mots de passe en clair pendant l'import | Partiel (inévitable) | — | — | Hors v2 |
| I19 | Tous | Matrice VM / PC réel et preuves exigées | Corrigé (procédure) | — | **Aucun essai réel fait** | Recette par lot (T01 à T26) |
| I20 | Tous | Inventaire des composants tiers ; Cinebench/Blender seulement signés | Corrigé (code) | — | 7.6 : à faire ; AdwCleaner usage pro à confirmer | À tenir à jour pour Atelier (7-Zip, VLC, options) |
| M01 | PC Inspector → Audit | Écritures listées ; test disque sauté sous 5 Go libres | Corrigé (tests auto) | `DiskWriteGuardTests` | 7.5 : à faire | Renforcé : aucun test de performances dans Audit |
| M02 | PC Sécurité → Audit | Restes d'analyse purgés | Corrigé (tests auto) | `LeftoverPurgeTests` | 6.5 : à faire | Renforcé : aucune copie de la base navigateur dans Audit |
| M03 | Tous | Tableau des données et durées de conservation | Partiel | — | — | Durée réglable dans Atelier et Audit |
| M04 | CI | Actions épinglées par SHA, droits minimaux | Corrigé (CI) | CI | — | Appliqué aux nouvelles étapes |

## Garde-fous propres à la v2

| Garde-fou | Preuve automatisée | Essai réel |
|---|---|---|
| La bibliothèque commune `JvtechLecture` n'écrit ni registre, ni service, ni réglage Windows | `SeparationTests` (code source et dépendances) | — |
| JVTECH Audit n'inclut pas `JvtechAtelier.Actions` et ne demande pas les droits administrateur | `SeparationTests` | T11, T22 : à faire |
| Une action d'Atelier note l'état avant, l'état après, le code retour et la durée | `ActionJournalTests` | Lot 1 |
| JVTECH Audit n'écrit qu'un fichier : le rapport exporté, à l'endroit choisi dans « Enregistrer sous » | `SeparationTests.Audit_SeuleEcriture_LeRapportChoisiParLeTechnicien` | T22 : à faire |
| Le rapport d'Audit ne contient ni mot de passe, ni clé BitLocker, ni secret Wi-Fi (jamais lus) ; anonymisation possible | `V2RapportAuditTests` | T25 : à faire |
| Audit ne déclare jamais « Conforme » une information non lue (WMI muet, droits) ; poste administré sans fausse anomalie | `V2AuditTests` (RienDeLu, T05) | T05, T11 : à faire |

## Comment ajouter une preuve d'essai réel

Dans la colonne « Essai réel », remplacer « à faire » par : `date · VM ou PC de test (modèle) · scénario · résultat`
et ranger la capture ou le journal dans `docs/preuves/<point>-<date>/` (sans aucune donnée client).
