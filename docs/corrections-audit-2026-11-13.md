# Corrections après l'audit externe du 13/11/2026 (rapport sur la v1.0.94)

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

## Restent à traiter (lots suivants du rapport)

I03 à I05, I07 à I20, M01 à M04. Non traité dans ce lot : l'autorisation individuelle de chaque PC sur le serveur (liste blanche MAC), recommandée en complément de C01, et l'isolement réseau (VLAN), qui est une mesure d'installation.
