# Protocole de validation sur PC réels

Les tests automatisés (`tests/JvtechTests`, lancés par GitHub Actions à chaque compilation) vérifient les règles de
décision avec des données simulées. Ils ne remplacent pas un essai sur un vrai PC : pilotes, droits, langues,
disques et réseaux réels ne se simulent pas. Une fonction n'est « validée » qu'après le scénario ci-dessous, noté
dans le tableau de suivi en bas de page.

**Règles** : jamais sur le PC d'un client ni sur un PC de production. Préparation revente et tests destructeurs
uniquement sur un PC de test effacé ou dans une machine virtuelle (Hyper-V). Garder une sauvegarde de tout disque
utilisé pour la migration.

## Parc de test conseillé

| Repère | Matériel | Windows |
|---|---|---|
| A | Portable récent, SSD NVMe, Wi-Fi | Windows 11 24H2 ou 25H2 |
| B | PC fixe ancien (avant 2018), disque SATA (HDD ou SSD) | Windows 10 22H2 |
| C | Machine virtuelle Hyper-V (génération 2, TPM virtuel) | Windows 11, installée par PXE |
| D | PC avec contrôleur RAID / Intel RST ou disque en boîtier USB | Windows 10 ou 11 |

Comptes : un administrateur et un compte standard sur A et B.

## Scénarios

### 1. Préparation revente (PC Maintenance `/preparation`) — critique
| # | Scénario | Résultat attendu |
|---|---|---|
| 1.1 | C installé par PXE, mode audit, `PremierDemarrage=1` | Préparation complète, Sysprep à la fin, PC éteint |
| 1.2 | C, mode audit, sans options.ini | Préparation complète, **pas** de Sysprep |
| 1.3 | Lancer `PcMaintenance.exe /preparation` sur B (Windows normal) | Fenêtre « refusée », « Ne rien faire » par défaut ; rien n'est modifié ; clé Run `JVTECH-Preparation` absente |
| 1.4 | 1.3, taper un faux nom puis le bon nom | Bouton actif seulement avec le nom exact ; préparation **sans** Sysprep |
| 1.5 | Copier etat.ini confirmé de B vers A, lancer `/preparation` sur A | Refus (la confirmation ne vaut que pour B) |
| 1.6 | C, couper le courant pendant Windows Update | Reprise au redémarrage, journal cohérent |
| 1.7 | Lancer en compte standard | Refus propre (droits), aucune action |
| 1.8 | C en mode audit : relever `ImageState` (HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Setup\State) | Valeur dans la liste blanche (`…_RESEAL_TO_AUDIT` ou `UNDEPLOYABLE`) ; sinon la préparation est refusée (à signaler) |
| 1.9 | B avec un plan d'alimentation personnalisé actif, préparation confirmée hors audit | À la fin : même plan actif, réglages identiques (`powercfg /list`, `/q`), plan temporaire supprimé |
| 1.10 | B avec `DODownloadMode=3` déjà posé par une stratégie | À la fin : `DODownloadMode` toujours à 3 (REG_DWORD) |

### 2. Serveur PXE — critique
| # | Scénario | Résultat attendu |
|---|---|---|
| 2.1 | Démarrage PXE de C, « Accepter les PC » ouvert, taper EFFACER | Installation, suivi visible dans le serveur |
| 2.1b | Démarrage PXE de C, ne rien taper / taper autre chose | Le PC s'éteint, disque intact |
| 2.1c | Démarrage PXE d'un PC à 2 disques (ou avec une clé USB) | Refus « pas exactement UN disque », PC éteint, disques intacts |
| 2.1d | Serveur préparé avant le 13/11/2026 | « Serveur à préparer » : impossible d'accepter des PC avant d'avoir refait l'image |
| 2.2 | « Accepter les PC » fermé depuis plus de 15 min | Le PC ne reçoit plus rien en TFTP |
| 2.3 | Depuis un autre PC du réseau : `Invoke-WebRequest http://serveur:8081/jvtech/etat -Method Post -Body "Serie=X"` sans jeton | Refus 401, ligne de refus dans le journal, jeton jamais écrit |
| 2.4 | Même requête depuis un autre sous-réseau (ou VPN) | Refus 403 |
| 2.5 | Envoyer 6 Mo sur `/jvtech/rapport` avec le jeton | Refus 413 |
| 2.6 | Ouvrir serveur.ini | Pas de mot de passe ni de jeton en clair (`MotDePasseProtege`, `JetonProtege`) |
| 2.7 | `icacls C:\JVTECH-PXE` | Seuls Administrateurs et SYSTEM |
| 2.8 | Ouvrir une session Windows avec jvtechpxe | Impossible (pas dans le groupe Utilisateurs) |

### 3. PC Migration — critique
| # | Scénario | Résultat attendu |
|---|---|---|
| 3.1 | Sauvegarde complète A → disque USB, avec mot de passe | Rapport « complet », fichiers `.jvenc` pour Wi-Fi et mots de passe, **aucun** CSV ni XML en clair sur le disque |
| 3.2 | Restauration sur B | Wi-Fi reconnectés, documents présents, CSV importé puis effacé |
| 3.3 | Fichier Excel ouvert pendant la sauvegarde | Rapport « INCOMPLET », chemin du fichier listé, message « ne pas effacer l'ancien PC » |
| 3.4 | Débrancher le disque USB en pleine copie | « ÉCHEC » ou « INCOMPLET », jamais « complet » |
| 3.5 | Disque presque plein | Échec annoncé, pas de réussite |
| 3.6 | Disque cible en FAT32 avec un fichier de plus de 4 Go | Fichier listé en erreur, rapport « INCOMPLET » |
| 3.7 | Chemin de plus de 260 caractères | Copié |
| 3.8 | Fermer PC Migration (gestionnaire des tâches) pendant l'export des mots de passe, relancer | Dossier `AppData\Local\JVTECH-Migration-secrets` vidé au lancement |
| 3.9 | Restaurer une sauvegarde faite avec une version antérieure (v1.0.8x) | Restauration normale |
| 3.10 | Mauvais mot de passe à la restauration | Refus avant de commencer |
| 3.11 | Sur B, un `Documents\devis.docx` différent (plus ancien) de celui de la sauvegarde | Celui de B intact ; version de l'ancien PC dans « Fichiers de l'ancien PC en double (…) » ; listé dans le rapport |
| 3.12 | Même nom, même date, contenu différent | Les deux versions gardées |
| 3.13 | Chrome ouvert sur B (profil verrouillé) pendant la restauration des navigateurs, refuser de le fermer puis forcer | Chrome « NON restauré », profil de B intact, aucune fusion |

### 4. Diagnostic disques (PC Inspector)
| # | Scénario | Résultat attendu |
|---|---|---|
| 4.1 | A (NVMe) | Santé lue, aucun « contrôle impossible » |
| 4.2 | D (RAID / RST / USB) | « Contrôle impossible », note non pénalisée, pas de « défaillant » |
| 4.3 | B (SATA) | Compteurs SMART lus |
| 4.4 | Disque connu comme défaillant (stock atelier) | Alerte rouge |

### 5. PC Maintenance
| # | Scénario | Résultat attendu |
|---|---|---|
| 5.1 | Réparation DISM/SFC sur A (sain) | « sain », réussite |
| 5.2 | Réparation sans Internet | DISM : message « téléchargement impossible », échec annoncé |
| 5.3 | Réparation sur Windows en anglais | Verdict SFC correct (texte ou CBS.log) |
| 5.4 | Nettoyage du cache Windows Update | Services `wuauserv` et `bits` redémarrés à la fin (vérifier `services.msc`) |
| 5.5 | « Arrêter après l'étape » pendant une opération longue | Arrêt propre après l'étape en cours |
| 5.6 | AdwCleaner avec Internet coupé en cours de téléchargement | Pas de fichier `.part` restant, message clair |

### 6. PC Sécurité
| # | Scénario | Résultat attendu |
|---|---|---|
| 6.1 | A et B | Aucun « URGENT » injustifié ; points « NON CONCERNÉ » cohérents (pas de Wi-Fi sur B fixe) |
| 6.2 | Compte standard | Points illisibles en « NON VÉRIFIÉ », pas en rouge |
| 6.3 | DNS d'entreprise ou de VPN | « À AMÉLIORER », pas « URGENT » |
| 6.4 | Vérifier qu'aucun réglage n'est modifié (comparer un export `reg` avant/après) | Identique |

### 7. Journaux et distribution
| # | Scénario | Résultat attendu |
|---|---|---|
| 7.1 | Lanceur → « Journaux pour le support » | .zip créé, aucune ligne contenant un mot de passe |
| 7.2 | Télécharger une version, comparer `Get-FileHash` avec SHA256SUMS.txt | Identique |
| 7.3 | Mode clé USB : lancer depuis une clé | Rapports et journaux sur la clé |

## Suivi

| Scénario | Date | PC | Version | Résultat | Remarque |
|---|---|---|---|---|---|
| (à remplir) | | | | | |

État au 10/11/2026 : **aucun de ces scénarios n'a encore été exécuté sur un PC réel.** Seuls la compilation et les
tests automatisés ont été vérifiés.
