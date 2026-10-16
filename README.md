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

## Obtenir l'exécutable

- **Sans rien installer** : chaque push sur GitHub lance l'action *Build*, qui produit `PcInspector.exe`
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
