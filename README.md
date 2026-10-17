# PC Inspector

Petit logiciel Windows façon Speccy : il affiche toutes les informations matérielles du PC
(puis logicielles plus tard) et permet d'exporter un rapport.

## Avancement

1. **Cadrage** : projet C# / WPF sur .NET Framework 4.8, un seul `.exe` léger. ✅
2. **Hardware** : un onglet par composant (CPU, carte mère/BIOS, RAM, GPU + écrans, disques + SMART, réseau, températures). ✅
3. **Diagnostic métier** : périphériques en erreur, plantages, batterie, compatibilité Windows 11, licence et sécurité, conseils d'upgrade, lien garantie. ✅
4. **Export** : rapport client HTML et TXT (logo, client, n° d'intervention). ✅
5. **Logiciel** : système, logiciels installés, pilotes, services, démarrage. ✅

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
Il ne modifie rien sur le PC. Le bouton « Exporter le rapport » produit le même rapport client que PC Inspector.

Il partage le code WMI, le rapport et la fenêtre d'export avec PC Inspector (fichiers liés dans le `.csproj`).

## Obtenir l'exécutable

- **Sans rien installer** : chaque push sur GitHub lance l'action *Build*, qui produit `PcInspector.exe` et `PcSecurite.exe`
  dans les artefacts du run (onglet *Actions* du dépôt).
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
