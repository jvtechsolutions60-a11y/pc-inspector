# Corrections après l'audit externe du 13/11/2026 (rapport sur la v1.0.94)

Les 28 points du rapport sont traités : lot bloquant ci-dessous, puis lots suivants.


Lot bloquant traité : C01 à C04, I01, I02, I06. Chaque point a d'abord été vérifié dans le code : **tous confirmés**.
Rien de ce lot n'a été testé sur une machine réelle : scénarios ajoutés au protocole (1.8 à 1.10, 2.1b à 2.1d, 3.11 à 3.13).

| Point | Correction | Fichiers | Vérification |
|---|---|---|---|
| C01 PXE efface le disque 0 de tout PC démarré pendant la fenêtre | Windows PE liste les disques (diskpart) : **refus et arrêt s'il n'y a pas exactement un disque** ; affiche le détail du disque 0 ; **installation seulement si EFFACER est tapé** sur le poste, sinon arrêt sans rien effacer. Une image préparée avant ce changement n'est plus utilisée (`VersionWinPE`) : à refaire depuis l'ISO | `JvtechServeur/Pxe/ImageBuilder.cs` (`WinPeDiskCheck`, `WinPeScriptVersion`), `ServerConfig.cs`, `deploy/LISEZMOI.txt` | `PxeScriptTests.cs` (ordre du script, EFFACER, refus multi-disques). **Non testé dans un vrai Windows PE** |
| C02 mode audit accepté avec ImageState absent | Audit seulement si `AuditInProgress=1` **et** `ImageState` dans la liste blanche (`UNDEPLOYABLE`, `SPECIALIZE_RESEAL_TO_AUDIT`, `GENERALIZE_RESEAL_TO_AUDIT`) ; sinon « inconnu » = refus | `PcMaintenance/Preparation/PreparationGuard.cs` | `PreparationTests.cs` (null, vide, inconnu, contradictoire) |
| C03 /XO écrase un fichier plus ancien | Restauration **sans aucun écrasement** (`/XC /XN /XO`) ; fichiers déjà présents et différents : version de l'ancien PC copiée dans « Fichiers de l'ancien PC en double (date) », listés dans le rapport | `PcMigration/Migration/Tools.cs` (`SaveConflicts`), `Restore.cs` | `RobocopyTests.cs` (arguments, conflit réel sur fichiers temporaires) |
| C04 fusion du profil navigateur si le déplacement échoue | Navigateur **non restauré**, profil existant intact, noté en problème | `Restore.cs` | Lecture du code |
| I01 `powercfg /restoredefaultschemes` | Copie temporaire du plan actif, réglages de veille sur la copie, plan d'origine réactivé et copie supprimée à la fin | `PreparationRunner.cs` | Lecture du code |
| I02 stratégies supprimées sans relevé | Existence, type et valeur relevés **une seule fois** (dans etat.ini), remis exactement ; type inconnu = laissé | `PolicyBackup.cs` (nouveau), `PreparationState.cs`, `PreparationRunner.cs` | `PolicyBackupTests` |
| I06 secrets écrits si les droits du dossier échouent | Droits relus après pose (héritage coupé, uniquement SYSTEM / Administrateurs / client) ; sinon dossier supprimé et erreur **avant** tout déchiffrement | `Secrets.cs`, `Restore.cs` | `SecretsFolderTests` |

## Changements visibles pour le technicien

- PXE : une question sur chaque PC (taper EFFACER). Un PC avec deux disques ou une clé USB branchée est refusé.
- Serveur déjà préparé : refaire « Préparer depuis une ISO » une fois.
- Migration : plus aucun fichier du nouveau PC n'est remplacé ; les doublons arrivent dans un dossier à part.

## Lots suivants : I03 à I20, M01 à M04

Chaque point a été vérifié dans le code avant correction. **Rien n'a été testé sur une machine réelle** : scénarios
ajoutés au protocole (2.9 à 2.12, 3.14, 3.15, 5.7 à 5.11, 6.5, 7.4 à 7.6) et matrice VM / PC réel.

| Point | Correction | Fichiers | Vérification |
|---|---|---|---|
| I03 Sysprep fermé par nom | Seul le `sysprep.exe` officiel de System32 **lancé sans arguments** (fenêtre du mode audit) est fermé, identifié par WMI | `PreparationRunner.cs` (`IsAuditSysprepWindow`) | `SysprepWindowTests` |
| I04 nettoyage de fichiers utiles | Fichiers en lecture seule ignorés ; documents (Office, PDF, images…) trouvés dans Temp **déplacés** dans `Documents publics\JVTECH - mis de côté`, jamais supprimés | `Cleanup.cs` (`IsDocument`) | `CopyCheckTests` |
| I05 copie non vérifiée | Après chaque dossier : taille de **tous** les fichiers comparée, SHA-256 d'un fichier sur 20 (100 au plus) ; écart = « CONTRÔLE ÉCHOUÉ » | `PcMigration/Migration/Tools.cs` (`Verify`) | `CopyCheckTests` |
| I06 | (lot bloquant) | | |
| I07, I18 secrets, SSD, antivirus | Avertissement dans le rapport : sur SSD l'effacement d'un fichier n'est pas garanti au niveau physique ; l'antivirus peut analyser le CSV pendant son court passage | `Backup.cs`, `Restore.cs` | Lecture du code |
| I08 compte du partage permanent | `jvtechpxe` **désactivé** hors préparation, activé avec « Accepter les PC » et tant qu'un poste est en cours | `JvtechServeur/MainWindow.xaml.cs` (`SetShareAccount`) | Lecture du code, scénario 2.10 |
| I09 Win11Debloat sans empreinte | Téléchargé **par commit** ; empreinte des scripts contrôlée par la CI et inscrite dans PC Maintenance, qui **refuse** de lancer des scripts différents (ou une compilation sans empreinte) | `build.yml`, `Win11Debloat.cs` (`FolderHash`) | `Win11DebloatHashTests` (même valeur calculée hors .NET) |
| I10 pas de chaîne de confiance | **Attestation de provenance signée** (Sigstore) de chaque fichier publié ; procédure de vérification. Signature Authenticode : décision à prendre (coût) | `build.yml`, `docs/verification-telechargement.md` | À vérifier à la première publication sur `main` (scénario 7.4) |
| I11 PC inconnus acceptés | Chaque PC inconnu doit être **autorisé sur le serveur** (question Oui/Non) pendant la fenêtre « Accepter les PC » | `JvtechServeur/MainWindow.xaml.cs` | Lecture du code, scénario 2.9 |
| I12 enfants non arrêtés | Programme et descendants rangés dans un **job Windows**, tous arrêtés au délai dépassé | `ProcessRunner.cs` | Lecture du code, scénario 5.11 |
| I13 blocage par l'appelant | Lignes passées par une file bornée lue par un fil dédié ; une erreur de l'appelant n'arrête plus la lecture | `ProcessRunner.cs` | Lecture du code |
| I14 mises à jour d'image concurrentes | Une seule à la fois (verrou), dossier de travail unique supprimé à la fin | `ImageUpdater.cs` | Lecture du code |
| I15 URL / version | HTTPS obligatoire, hôtes Microsoft uniquement, nom de fichier contrôlé, **refus d'une version plus ancienne** | `ImageUpdater.cs` (`SecureUrl`, `Downgrade`) | `ImageUpdaterTests` |
| I16 secrets illisibles ignorés | Détectés : alerte, image considérée à refaire, aucun PC accepté | `ServerConfig.cs` (`SecretsLost`) | Lecture du code, scénario 2.12 |
| I17 rapport tronqué / illisible | 200 lignes au plus dans le rapport + lien vers la liste complète ; sortie complète de robocopy gardée dans « Journaux robocopy » | `Tools.cs`, `JobContext.cs` | Scénario 3.15 |
| I19 tests réels absents | Matrice VM / PC réel, preuves exigées, scénarios ajoutés | `docs/protocole-validation.md` | — |
| I20 licences | Inventaire complet : versions, empreintes, licences, contrôles ; Cinebench et Blender lancés **seulement s'ils sont signés** | `THIRD-PARTY-NOTICES.md`, `PerformanceCollector.cs` | Conditions d'usage pro d'AdwCleaner à confirmer chez l'éditeur |
| M01 « lecture seule » | Distinction « aucune modification de configuration » / « écritures temporaires », liste complète ; test d'écriture **sauté si moins de 5 Go libres** | `docs/donnees-et-conservation.md`, `PerformanceCollector.cs` | `DiskWriteGuardTests` |
| M02 copie de la base navigateur | Emplacement et droits documentés ; restes d'une analyse interrompue **purgés** à l'analyse suivante | `SecurityChecks.Software.cs` (`PurgeLeftovers`) | `LeftoverPurgeTests` |
| M03 conservation | Tableau des données personnelles, durées conseillées, règles RGPD de l'atelier | `docs/donnees-et-conservation.md` | — |
| M04 chaîne CI | Actions épinglées par SHA, droits en lecture seule, publication dans un travail séparé seul autorisé à écrire | `build.yml` | Compilation |

Choix assumés (pas de changement) : pas de « mode strict » dans PC Inspector, pas de mode « sans copie » dans PC
Sécurité, pas d'anonymisation automatique des rapports (raisons dans `docs/donnees-et-conservation.md`). Isolement
réseau du serveur PXE (VLAN) : mesure d'installation, conseillée.
