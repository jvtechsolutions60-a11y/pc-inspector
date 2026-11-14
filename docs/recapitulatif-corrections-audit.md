# JVTECH Outils : corrections après audit externe (10/11/2026)

Branche `claude/project-thread-abzfuu`, PR #13 (non fusionnée). Base : v1.0.83 (de032f5), tous les correctifs
précédents conservés. Commits : b3d7ca6 (sécurité), 1936649 et 62f8198 (fiabilité), ae4d9c3 et suivants (qualité,
tests, documentation).

Compilation v1.0.87 : les 6 logiciels compilent et **119 tests automatisés sur 119 réussissent** (GitHub Actions, run 87).

**Important : rien n'a été testé sur un PC Windows réel.** « Compile » veut dire que GitHub Actions a compilé ;
« test automatisé » veut dire qu'un test avec données simulées passe dans la CI. Le protocole de validation sur PC
réels est dans `docs/protocole-validation.md`.

## Étape 1 — Sécurité

| Point | Problème | Correctif | Fichiers | Vérifié |
|---|---|---|---|---|
| /preparation (critique) | Lancée sur n'importe quel PC, pouvait déclencher Sysprep sur un PC client | Lecture du mode audit (AuditInProgress + ImageState, les deux doivent concorder) ; hors audit : refus par défaut, confirmation en tapant le nom exact du PC, et **jamais** de Sysprep hors audit ; Sysprep seulement si `PremierDemarrage=1` écrit exactement ; mode revérifié juste avant Sysprep ; reprise automatique retirée en cas de refus | PcMaintenance : Preparation/PreparationGuard.cs (nouveau), PreparationRefusalWindow.cs (nouveau), App.xaml.cs, PreparationOptions.cs, PreparationState.cs, PreparationRunner.cs, PreparationWindow.xaml.cs | Compile, 34 cas de tests automatisés |
| Suivi PXE port 8081 (critique) | Remontées acceptées de n'importe qui, sans limite | Jeton secret 256 bits (en-tête X-JVTECH-Jeton, comparaison en temps constant), sous-réseau local seulement (application + pare-feu `LocalSubnet`), POST seulement, 16 Ko / 5 Mo maximum, champs filtrés, 120 requêtes/min par adresse, refus journalisés sans le jeton | JvtechServeur : Pxe/StatusPolicy.cs (nouveau), StatusServer.cs, ImageBuilder.cs, MainWindow.xaml.cs ; PcMaintenance : Preparation/ServerLink.cs | Compile, tests automatisés |
| Mot de passe jvtechpxe | En clair dans serveur.ini | Chiffré DPAPI (machine) dans serveur.ini, ancien fichier migré automatiquement ; compte retiré du groupe Utilisateurs (lecture du partage seulement) ; C:\JVTECH-PXE réservé aux administrateurs ; TFTP fermé 15 min après « Accepter les PC » | ServerConfig.cs, ImageBuilder.cs, TftpServer.cs | Compile |
| Secrets de migration (critique) | CSV de mots de passe et clés Wi-Fi en clair sur le disque externe ou le Bureau | Wi-Fi lus et ajoutés par l'API Windows en mémoire ; CSV dans un dossier local réservé (pas synchronisé), chiffré puis écrasé, nettoyé même en cas d'erreur et au lancement suivant ; format des sauvegardes inchangé (anciennes sauvegardes compatibles) | PcMigration : Migration/Secrets.cs (nouveau), Backup.cs, Restore.cs, MainWindow.xaml.cs | Compile, chiffrement testé (aller-retour, mauvais mot de passe, fichier modifié) |

## Étape 2 — Fiabilité

| Point | Correctif | Fichiers | Vérifié |
|---|---|---|---|
| NVMe | Délai dépassé, pilote RAID/RST/USB, accès refusé = « contrôle impossible », sans pénalité ; seuls les indicateurs du journal du SSD comptent comme panne ; résultats gardés 1 min | PcInspector/Collectors/DiskHealth.cs | Compile, tests automatisés (journaux NVMe et SMART simulés) |
| DISM / SFC | 3010 = réussite avec redémarrage ; codes d'erreur traduits ; SFC lu en français, en anglais, puis dans CBS.log (indépendant de la langue) ; résultat inconnu = jamais « réussi » | PcMaintenance/Maintenance/Repair.cs, MainWindow.xaml.cs, CommandLine.cs | Compile, tests automatisés |
| Cache Windows Update | Cache vidé seulement si wuauserv est bien arrêté ; services redémarrés et vérifiés dans tous les cas | Maintenance/Cleanup.cs | Compile |
| Robocopy | Tous les codes traduits ; fichiers en erreur listés dans le rapport ; « INCOMPLET », « ÉCHEC » ou « NON VÉRIFIÉ » dès qu'il manque un fichier ; message « ne pas effacer l'ancien PC » ; /MIR, /PURGE, /MOV interdits (vérifié par test) | PcMigration/Migration/Tools.cs, Backup.cs, Restore.cs, MainWindow.xaml.cs | Compile, tests automatisés |
| PC Sécurité | Statuts « NON CONCERNÉ » et « NON VÉRIFIÉ », hors note ; références réunies et datées dans References.cs, « NON VÉRIFIÉ » si plus d'un an ou version de Windows inconnue ; DNS inconnu = « À AMÉLIORER » | PcSecurite/Checks/*.cs, StatusBrushConverter.cs, docs/references-pc-securite.md | Compile, tests automatisés |

## Étape 3 — Qualité technique

| Point | Correctif | Fichiers | Vérifié |
|---|---|---|---|
| Journaux | Conservation 90 jours, 5 Mo maximum par jour, mots de passe / jetons / `net user` masqués avant écriture ; lien « Journaux pour le support » dans le lanceur (.zip des 14 derniers jours) | PcInspector/CrashGuard.cs, JvtechOutils/SupportExport.cs (nouveau), MainWindow.xaml(.cs) | Compile, masquage testé |
| Solution | PcInspector.sln contient les 6 logiciels + les tests ; la CI compile toujours module par module | PcInspector.sln | Compile |
| SDK | `global.json` (SDK .NET 8.0.x), installé explicitement dans la CI | global.json, build.yml | Compile |
| Distribution | Empreintes SHA-256 jointes ; AdwCleaner téléchargé en `.part` puis vérifié | build.yml, AdwCleaner.cs, docs/signature-et-distribution.md | Compile |

## Étape 4 — Validation

- Projet `tests/JvtechTests` (MSTest, .NET Framework 4.8) : préparation, serveur PXE, disques, DISM/SFC, robocopy,
  chiffrement de la migration, note et références de sécurité, masquage des journaux. Lancé à chaque compilation ;
  un échec bloque la production des .exe.
- Protocole sur PC réels : `docs/protocole-validation.md` (42 scénarios, Windows 10 et 11, portable et fixe, SATA et
  NVMe, compte standard, sans Internet, disque plein, migration interrompue, préparation en machine virtuelle).

## Points non vérifiés

- Tout le comportement réel sur Windows : interface, droits, registre, WMI, API Wi-Fi, robocopy, DISM, SFC, PXE.
- La fenêtre de refus de /preparation (construite en code) n'a jamais été affichée.
- L'API Wi-Fi (wlanapi) : lecture et ajout de profils non essayés sur une vraie carte Wi-Fi.
- Le masquage des journaux couvre les formes connues (`MotDePasse=`, `Jeton=`, `/p:`, `net user`) : une autre forme
  de secret pourrait passer.

## Risques résiduels

- Le mot de passe de jvtechpxe reste lisible dans l'image Windows PE (boot.wim) : inévitable pour monter le
  partage. Atténué par un compte en lecture seule, sans ouverture de session, TFTP fermé hors fenêtre d'acceptation.
- Sur SSD, l'écrasement d'un fichier ne garantit pas l'effacement physique : les fichiers en clair ne restent que
  quelques secondes, dans un dossier local réservé.
- DISM et SFC ne peuvent pas être interrompus proprement en cours de route (« Arrêter après l'étape » existe).
- Les .exe ne sont pas signés (avertissement SmartScreen) : décision à prendre, voir docs/signature-et-distribution.md.
- Chiffrement facultatif des documents de la migration : étudié, non fait. Conseil : disque externe chiffré
  BitLocker To Go, plus simple et plus fiable qu'un chiffrement maison.

## À valider sur Windows réel avant toute utilisation client

1. Préparation revente en machine virtuelle (scénarios 1.1 à 1.7), surtout le refus sur un PC normal.
2. Migration complète, puis interrompue et avec fichier ouvert (3.1 à 3.10).
3. Suivi PXE avec et sans jeton (2.1 à 2.8).
4. Disques NVMe, SATA et derrière RAID/USB (4.1 à 4.4).
