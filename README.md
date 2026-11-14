# JVTECH Outils : diagnostic, sécurité, maintenance et migration de PC Windows

Boîte à outils gratuite d'un dépanneur informatique pour Windows 10 et 11 : un `.exe` léger par tâche, sans installation,
utilisable depuis une clé USB, avec un rapport client en HTML, PDF ou texte.

| Logiciel | À quoi il sert | Modifie le PC ? |
|---|---|---|
| **PC Inspector** | Matériel et logiciel du PC (façon Speccy), santé des disques (SMART), bilan sur 100, devis d'upgrade, fiche de suivi, texte d'annonce | Non |
| **PC Sécurité** | Contrôle de sécurité (antivirus, mises à jour, BitLocker, TPM, mots de passe…) avec une note et un pas à pas par point | Non |
| **PC Maintenance** | Nettoyage, AdwCleaner, mises à jour Windows et logiciels (winget), pack logiciels, passage à Windows 11, préparation revente | Oui |
| **PC Migration** | Ancien PC → disque externe → nouveau PC : dossiers, navigateurs, mots de passe, applications, Wi-Fi, Outlook | Oui (rien n'est supprimé) |
| **JVTECH Outils** | Lanceur des quatre logiciels | Non |
| **JVTECH Serveur PXE** | Préparation automatique des PC par le réseau, à l'atelier | Oui (PC acceptés seulement) |

**Télécharger** : page [Releases](https://github.com/jvtechsolutions60-a11y/pc-inspector/releases/latest), fichier
`JVTECH-Outils-v1.0.N.zip`, à décompresser puis lancer `JvtechOutils.exe`.

Mots-clés : diagnostic PC, dépannage informatique, Speccy, SMART, test disque dur, sécurité Windows, nettoyage PC,
winget, mise à jour Windows 11, migration de données, transfert de PC, rapport client.

## PC Inspector

Petit logiciel Windows façon Speccy : il affiche toutes les informations matérielles et logicielles du PC
et permet d'exporter un rapport.

### Avancement

1. **Cadrage** : projet C# / WPF sur .NET Framework 4.8, un seul `.exe` léger. ✅
2. **Hardware** : un onglet par composant (CPU, carte mère/BIOS, RAM, GPU + écrans, disques + SMART, réseau, températures). ✅
3. **Diagnostic métier** : périphériques en erreur, plantages, batterie, compatibilité Windows 11, licences (et BitLocker), conseils d'upgrade, lien garantie. ✅
   Depuis novembre 2026, la sécurité détaillée (antivirus, pare-feu, mises à jour en attente…) n'est plus en double ici :
   le bilan garde les points essentiels et son bouton « Ouvrir PC Sécurité » mène au contrôle complet. Les anciens
   « Conseils » sont fondus dans le bilan.
4. **Export** : rapport client HTML et TXT (logo, client, n° d'intervention). ✅
5. **Logiciel** : système, logiciels installés, pilotes, services, démarrage. ✅
6. **Bilan en 1 clic** : écran d'accueil avec une note sur 100, les points rouges en tête et l'export du rapport client. ✅
7. **Outils inspirés de NirSoft** : licences Windows et Office, débit internet, temps de démarrage, espace disque. ✅
8. **Santé des disques façon Scrutiny** : secteurs réalloués, en attente et irrécupérables, erreurs CRC et alerte SMART
   des disques SATA ; alerte critique, réserve, vie consommée et erreurs média des SSD NVMe. Chaque compteur est
   noté vert, orange ou rouge avec des seuils inspirés des statistiques de pannes Backblaze, et les problèmes
   remontent dans le bilan et le rapport client. Lecture seule. ✅
9. **Mises à jour des logiciels (winget)** : onglet qui liste les logiciels ayant une version plus récente
   (version installée et version disponible), lu avec `winget upgrade` sans argument, ce qui n'installe rien.
   Le bilan et le rapport client le reprennent, avec la commande `winget upgrade --all` en conseil.
   Si winget est absent, l'onglet l'indique avec le lien du Microsoft Store. ✅
10. **Évolutions d'octobre 2026** ✅ :
    - onglet **Pilotes manquants** : périphériques sans pilote, identifiant matériel (PCI\VEN_…&DEV_…), fabricant de la puce
      et lien de recherche ;
    - onglet **Devis d'upgrade** : la barrette (type, format, fréquence, taille) ou le SSD exactement compatibles, avec une
      fourchette de prix et la main d'œuvre, modifiables dans `prix.ini` (lien « Modifier les prix ») ;
    - **Outils** dans la barre latérale : **test des périphériques** (écran plein écran par couleur, clavier touche par touche,
      haut-parleurs gauche/droite, niveau du micro, webcam), **suivi en direct** (courbes charge, mémoire, températures sur
      5 minutes) et **étiquette PC** au format carte de visite à coller sous le PC ;
    - export du rapport en **PDF** (Microsoft Edge en arrière-plan), en plus du HTML et du texte.

11. **Onglet Performances** (novembre 2026) ✅ : mesures seulement, rien n'est modifié.
    - **Test bureautique** (≈ 3 min, lancé tout seul au démarrage, après le bilan) : notes Windows (WinSAT),
      processeur avec 7-Zip (`7zr.exe`, LGPL, embarqué), lecture du disque (WinSAT) et écriture de 1 Go sans cache,
      puis un verdict (« Très fluide », « Lent, SSD conseillé »…) ;
    - **Test gaming complet** (≈ 20 min) : en plus, Cinebench (scores mono et multi, puis 10 minutes de charge où la
      fréquence du processeur et les températures sont relevées) et Blender Benchmark (GPL) sur la carte graphique.
      Le bouton reste disponible pendant le test bureautique : le test gaming attend sa fin et reprend ses mesures.
      Ces deux outils gratuits ne sont pas redistribuables : on les dépose une fois dans le dossier `Outils` à côté de
      `PcInspector.exe`. Blender télécharge ses scènes à la première utilisation ;
    - les résultats vont dans le rapport, la fiche de suivi et le Texte Leboncoin.
    Écartés : UserBenchmark, UserDiag (pas automatisables), OCCT, FurMark 2, 3DMark et PCMark (payants pour un atelier).

Ergonomie : onglets classés Matériel / Diagnostic / Logiciel, recherche dans l'onglet (Ctrl+F), bouton Copier
(texte prêt à coller dans un devis) et Actualiser (F5).

## PC Sécurité

Deuxième petit logiciel, dans `src/PcSecurite` : un seul écran qui contrôle la sécurité du PC avec un feu
vert, orange ou rouge par point et une note sur 100. Il vérifie la version de Windows (fin de support),
l'antivirus, le pare-feu, les mises à jour, l'activation, l'UAC, SmartScreen, BitLocker, Secure Boot, le TPM,
SMB1, le Bureau à distance, les comptes (Invité, sans mot de passe) et la connexion automatique.

Veille d'octobre 2026, ajoutée ensuite : protections de Microsoft Defender (temps réel, falsification,
exclusions suspectes), intégrité de la mémoire, protection LSA, blocage des pilotes vulnérables, WDigest,
certificats Secure Boot 2023 (ceux de 2011 ont expiré en juin 2026), âge du BIOS, PowerShell 2.0, logiciels de
prise en main à distance (arnaques au faux support), fichier hosts et proxy, Wi-Fi ouverts enregistrés,
navigateurs pas à jour, logiciels avec failles exploitées (WinRAR, 7-Zip, Notepad++, PuTTY, VLC) et logiciels
abandonnés (Flash, Java 6/7, vieux Acrobat, Office 2016, 2019 et 2021…).

Ajoutés ensuite : **serveurs DNS** (box, opérateurs et DNS publics connus ; un DNS inconnu est signalé en rouge) et
**tâches planifiées suspectes** (PowerShell caché, mshta, programmes lancés depuis Temp, AppData ou ProgramData).

Les résultats sont triés : d'abord les **corrections à faire** (réglages, mises à jour), puis ce qu'il faut
**changer ou remplacer** (Windows plus supporté, logiciel abandonné, vieux TPM), puis les points en ordre. La note est aussi donnée en classe de A à E, façon Nutri-Score.
Chaque point rouge ou orange a un tutoriel « Comment faire ? » pas à pas, avec un bouton qui ouvre le bon réglage
de Windows. Les tutoriels sont aussi repris dans le rapport client.

- **Mode guidé** : passe les points à corriger un par un. Le technicien corrige lui-même, puis « Vérifier » refait
  seulement ce contrôle. Le rapport indique la note avant/après et ce qui a été corrigé pendant l'intervention.
- **Fiche conseils client** : un guide simple (HTML ou texte) des points restants, avec le pas à pas quand le client
  peut le faire, ou « à confier à votre technicien » pour le BIOS, le registre, etc.

PC Sécurité ne fait que lire et conseiller : il ne modifie jamais le PC. Les boutons « Ouvrir… » affichent seulement
la page de réglages de Windows.
Le bouton « Exporter le rapport » produit le même rapport client que PC Inspector.

Il partage le code WMI, le rapport et la fenêtre d'export avec PC Inspector (fichiers liés dans le `.csproj`).

## JVTECH Sonde

Pour les PC sous contrat de maintenance : un petit programme sans fenêtre (`src/JvtechSonde`, `JvtechSonde.exe`), lancé
chaque jour par une tâche planifiée. Il contrôle la mémoire, les températures, le remplissage et la santé des disques,
la batterie, Windows Update (sans rien installer), l'antivirus, le pare-feu et les plantages, puis envoie le résultat à
un moniteur « Push » d'Uptime Kuma. Lecture seule. Les mesures sont celles de PC Inspector et de PC Sécurité (fichiers
liés dans le `.csproj`). Module optionnel `src/JvtechSonde.Capteurs` : température précise du processeur avec
LibreHardwareMonitor, utilisé seulement si le pilote PawnIO est déjà installé. Mise en place et recette :
[docs/sonde.md](docs/sonde.md). Livré à part dans `JVTECH-Sonde-v1.0.N.zip`.

## Communs à tous les logiciels

- **Lanceur JVTECH Outils** (`src/JvtechOutils`, `JvtechOutils.exe`) : un écran d'accueil qui ouvre PC Inspector, PC Sécurité
  ou PC Maintenance, rangés dans le même dossier, et le dossier des rapports.
- **Dossier client commun** : les rapports sont proposés dans `Documents\JVTECH\Rapports\<client>\<date>`. Le client et le
  n° d'intervention saisis dans un logiciel sont repris dans les autres le même jour.
- **Mode clé USB** : lancés depuis une clé (ou avec un fichier `portable.txt` à côté des .exe), les logiciels gardent
  réglages, rapports et téléchargements dans `JVTECH\` sur la clé : rien n'est écrit sur le PC du client.
- **Mise à jour** : au démarrage, chaque logiciel regarde la dernière version publiée sur GitHub et affiche un lien
  « Nouvelle version » si elle est plus récente. Rien n'est téléchargé tout seul.
- **Charte commune** (`src/PcInspector/Theme.xaml`, partagé par les quatre .exe) : mêmes couleurs, boutons, cartes et en-têtes
  sombres partout, et les mêmes couleurs dans les rapports. Vert = en ordre, orange = à surveiller, rouge = à traiter.
- Version commune **v1.0.N** (N = numéro de compilation), affichée dans chaque logiciel et en pied de rapport.

## Obtenir l'exécutable

- **Sans rien installer** : chaque fusion dans `main` publie une version sur la page
  [Releases](https://github.com/jvtechsolutions60-a11y/pc-inspector/releases/latest) avec `JVTECH-Outils-v1.0.N.zip`
  (les quatre .exe). Chaque PR produit aussi l'artefact *JVTECH-Outils-v1.0.N* dans l'onglet *Actions*.
- **Sur un PC Windows avec le SDK .NET** (ou Visual Studio) :

  ```
  dotnet build src/PcInspector/PcInspector.csproj -c Release -o publish
  ```

  L'exécutable est dans `publish/PcInspector.exe`. Il utilise le .NET Framework 4.8, déjà présent sur
  Windows 10 et 11 : rien à installer sur le PC cible.

Le logiciel demande les droits administrateur au lancement, nécessaires pour lire l'état SMART des disques
et certaines sondes de température.

## Structure

- `src/PcInspector/Collectors/` : un collecteur par onglet, qui lit les infos (principalement via WMI).
- `src/PcInspector/Models/` : le format commun des infos (sections, groupes, lignes), réutilisé par l'export.
- `src/PcInspector/MainWindow.xaml` : la fenêtre (barre latérale + contenu de l'onglet).

## PC Maintenance

Troisième logiciel, `src/PcMaintenance/`, qui **agit** sur le PC (contrairement à PC Inspector et PC Sécurité,
qui ne font que des constats et des conseils). Il enchaîne, étape par étape :

1. un **point de restauration**, décoché par défaut (la protection du système est activée sur C: si elle était coupée) ;
2. **AdwCleaner** : dernière version téléchargée chez Malwarebytes (signature vérifiée), nettoyage automatique sans redémarrer ;
3. le **nettoyage intégré** (remplace Glary depuis le 31/10/2026) : fichiers temporaires, cache de Windows Update, nettoyage de disque Windows en silencieux, caches des navigateurs, raccourcis cassés mis de côté dans `Rapports\Raccourcis cassés`, analyse rapide Microsoft Defender et liste des programmes au démarrage dans le rapport. Favoris, mots de passe, historique, cookies, corbeille et Téléchargements ne sont jamais touchés, et le registre n'est pas « nettoyé » ;
4. la **recherche et l'installation des mises à jour Windows** (agent Windows Update, une par une, sans redémarrer) ;
5. **`winget upgrade --all`** en silencieux (source winget) ;
6. le **pack logiciels nouveau PC** (Chrome, 7-Zip, VLC, Acrobat Reader… à cocher, liste modifiable dans `pack.ini`).

Le bouton **Rapport de maintenance** produit la fiche client de ce qui a été fait : étapes et résultats, mises à jour
installées, logiciels mis à jour, espace disque avant/après, durée.

En haut, un résumé rapide : Windows 11 oui ou non, activation de Windows (résultat de `slmgr /xpr`), mises à jour Windows en attente, logiciels à mettre à jour,
espace libre sur le disque. Chaque étape peut être décochée, et un journal garde la trace de tout ce qui a été fait.

### Préparation revente (installation automatique par le réseau)

Pour les PC d'achat-revente : on efface tout et on réinstalle propre, sans clé USB et sans rester devant.
`JVTECH-Serveur-PXE-v1.0.N.zip` (publié avec chaque version) s'installe sur un mini PC Windows de l'atelier :

- **JVTECH Serveur PXE** (`src/JvtechServeur`) prépare tout depuis une ISO de Windows 11 avec les outils de Windows
  (DISM, bcdedit) : partage en lecture seule des fichiers d'installation, Windows PE modifié pour lancer l'installation
  depuis ce partage, et démarrage réseau compatible Secure Boot (proxyDHCP et TFTP intégrés, gestionnaire de démarrage
  signé Microsoft ; la box garde son rôle de DHCP). Les PC ne sont acceptés que pendant 30 minutes après un clic ;
- chaque jour il cherche la mise à jour cumulative de Windows 11 sur le catalogue Microsoft Update et l'intègre
  à l'image quand aucun PC n'est en cours ;
- son écran suit tous les PC (étape, durée, rapport) et compte les PC préparés par mois.

Sur le PC, `deploy/autounattend.xml` efface le disque 0, installe Windows 11 en français sans aucune question et
démarre en **mode audit**. `PcMaintenance.exe /preparation` enchaîne alors, en reprenant seul après chaque redémarrage :
nettoyage par [Win11Debloat](https://github.com/Raphire/Win11Debloat) (version figée, mode Sysprep) puis applications des fabricants retirées (`debloat.ini`), publicités et suggestions coupées, pilotes et BIOS du constructeur (Dell Command Update, Lenovo System
Update, HP Image Assistant, BIOS seulement sur secteur), Windows Update en boucle pilotes compris (partage des
mises à jour entre PC du réseau), nettoyage, écran à sa résolution la plus haute avec mise à l'échelle à 100 %, contrôle des pilotes et de l'activation, rapport envoyé au serveur.
À la fin, Sysprep remet le PC au **premier démarrage** et l'éteint : l'acheteur crée son propre compte.

Les étapes se règlent dans `options.ini`. Mode d'emploi : `deploy/LISEZMOI.txt`.

L'action *Build* compile aussi `PcMaintenance.exe` et `JvtechServeur.exe`.

## PC Migration

Passage d'un client de son ancien PC à un nouveau, par un disque externe (`PcMigration.exe`, administrateur).
Rien n'est supprimé, ni sur l'ancien PC ni sur le nouveau.

1. **Sauvegarder l'ancien PC** : compte Windows du client (celui ouvert à l'écran par défaut), disque de destination
   (place libre et FAT32 vérifiés), éléments à copier avec leur taille. Le tout va dans `X:\JVTECH Migration\Client - PC - date`.
2. **Restaurer sur le nouveau PC** : la sauvegarde est retrouvée sur le disque branché ; on coche ce qu'on remet.

| Élément | Sauvegarde | Restauration |
|---|---|---|
| Bureau, Documents, Images, Vidéos, Musique, Téléchargements | copie robocopy vérifiée (dossiers OneDrive suivis) | remis dans les dossiers du compte, un fichier plus récent déjà présent est gardé |
| Navigateurs (Chrome, Edge, Brave, Vivaldi, Opera, Firefox) | profil complet sans les caches : favoris, historique, extensions, onglets | profil existant renommé « avant migration », puis profil restauré |
| Mots de passe des navigateurs | export CSV guidé (Chrome/Edge les chiffrent avec le compte Windows), chiffré aussitôt | import guidé, fichier déchiffré effacé juste après ; Firefox : avec son profil |
| Applications | `winget export` + liste de tous les programmes | réinstallation silencieuse de celles cochées ; liste des autres dans le rapport |
| Wi-Fi | profils et clés (`netsh`), chiffrés | ajoutés pour tous les comptes |
| Outlook | signatures et `.pst` hors Documents | `Documents\Fichiers Outlook`, à ouvrir dans Outlook |
| Fond d'écran, imprimantes | image ; liste des imprimantes | image remise ; imprimantes dans le rapport (pilotes du fabricant) |

Mots de passe et clés Wi-Fi sont chiffrés avec le **mot de passe de la sauvegarde** (AES-256 + HMAC, PBKDF2) ; les
documents ne le sont pas. Un rapport « ce qui a été transféré » est enregistré d'office (dans la sauvegarde, puis dans
le dossier du client) et peut être exporté en PDF.

## JVTECH Diagnostic Cyber (archivé)

Mis de côté le 30/10/2026 : le code est conservé dans `archive/JvtechCyber/`, mais il n'est plus compilé ni proposé
dans le lanceur, et le bouton d'export de PC Sécurité est retiré. Pour le réactiver : remettre le dossier dans `src/`,
rajouter sa compilation dans `build.yml` et son bouton dans le lanceur (voir l'historique git du 28/10/2026).

Quatrième logiciel (`JvtechCyber.exe`) : la sécurité de **l'entreprise** (le système
d'information), là où PC Sécurité regarde un seul poste. Ne modifie rien sur le PC.

- **Un dossier par entreprise** dans `Rapports\Diagnostic cyber\<entreprise>`, avec l'historique des diagnostics et la
  progression de la note d'un diagnostic à l'autre.
- **Questionnaire en 9 thèmes**, chacun noté de 0 à 3 : sauvegardes, mises à jour, antivirus, comptes et mots de passe,
  arnaques, réseau et Wi-Fi, accès à distance et portables, inventaire et prestataires, incident et continuité.
  Référentiel JVTECH rédigé d'après les recommandations publiques de l'ANSSI et de cybermalveillance.gouv.fr.
- **Note sur 100** (les thèmes essentiels comptent double) et **6 menaces** (rançongiciel, fraude au virement, piratage de
  la messagerie, fuite de données, perte de données, faux support) marquées exposée, en partie couverte ou couverte.
- **Postes** : dans PC Sécurité, le bouton *Pour le diagnostic cyber* enregistre un fichier `.jvsec` par poste, que
  *Importer les postes* reprend. Les constats s'affichent sous chaque thème concerné.
- **Plan d'action et rapport PDF** : les prochaines étapes de chaque thème, de la plus urgente à la moins urgente, avec le
  coût indicatif et le prix JVTECH de la prestation (*Mes tarifs*, fichier `tarifs-cyber.ini`).
