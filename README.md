# PC Inspector

Petit logiciel Windows façon Speccy : il affiche toutes les informations matérielles du PC
(puis logicielles plus tard) et permet d'exporter un rapport.

## Avancement

1. **Cadrage** : projet C# / WPF sur .NET Framework 4.8, un seul `.exe` léger. ✅
2. **Hardware** : un onglet par composant (CPU, carte mère/BIOS, RAM, GPU + écrans, disques + SMART, réseau, températures). ✅
3. **Diagnostic métier** : périphériques en erreur, plantages, batterie, compatibilité Windows 11, licence et sécurité, conseils d'upgrade, lien garantie. ✅
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

## Communs aux trois logiciels

- **Lanceur JVTECH Outils** (`src/JvtechOutils`, `JvtechOutils.exe`) : un écran d'accueil qui ouvre PC Inspector, PC Sécurité
  ou PC Maintenance, rangés dans le même dossier, et le dossier des rapports.
- **Dossier client commun** : les rapports sont proposés dans `Documents\JVTECH\Rapports\<client>\<date>`. Le client et le
  n° d'intervention saisis dans un logiciel sont repris dans les autres le même jour.
- **Mode clé USB** : lancés depuis une clé (ou avec un fichier `portable.txt` à côté des .exe), les logiciels gardent
  réglages, rapports et téléchargements dans `JVTECH\` sur la clé : rien n'est écrit sur le PC du client.
- **Mise à jour** : au démarrage, chaque logiciel regarde la dernière version publiée sur GitHub et affiche un lien
  « Nouvelle version » si elle est plus récente. Rien n'est téléchargé tout seul.
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

1. un **point de restauration** (la protection du système est activée sur C: si elle était coupée) ;
2. **AdwCleaner** : dernière version téléchargée chez Malwarebytes (signature vérifiée), nettoyage automatique sans redémarrer ;
3. la **maintenance 1 clic de Glary Utilities**, si Glary est installé (la suite démarre quand sa fenêtre est fermée) ;
4. la **recherche et l'installation des mises à jour Windows** (agent Windows Update, une par une, sans redémarrer) ;
5. **`winget upgrade --all`** en silencieux (source winget) ;
6. le **pack logiciels nouveau PC** (Chrome, 7-Zip, VLC, Acrobat Reader… à cocher, liste modifiable dans `pack.ini`).

Le bouton **Rapport de maintenance** produit la fiche client de ce qui a été fait : étapes et résultats, mises à jour
installées, logiciels mis à jour, espace disque avant/après, durée.

En haut, un résumé rapide : Windows 11 oui ou non, activation de Windows (résultat de `slmgr /xpr`), mises à jour Windows en attente, logiciels à mettre à jour,
Glary installé ou non. Chaque étape peut être décochée, et un journal garde la trace de tout ce qui a été fait.

L'action *Build* compile aussi `PcMaintenance.exe`.
